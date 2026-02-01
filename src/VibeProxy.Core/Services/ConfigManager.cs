using System.Text;
using System.Text.Json;

namespace VibeProxy.Core.Services;

public sealed class ConfigManager : IDisposable
{
    private static readonly Dictionary<string, string> OAuthProviderKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = "claude",
        ["codex"] = "codex",
        ["gemini"] = "gemini-cli",
        ["github-copilot"] = "github-copilot",
        ["antigravity"] = "antigravity",
        ["qwen"] = "qwen"
    };

    private readonly VibeProxy.Core.Utils.Debouncer _configDebouncer;
    private FileSystemWatcher? _configWatcher;
    private string? _baseConfigPath;
    private SettingsStore? _settingsStore;
    private bool _isDisposed;

    public string AuthDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cli-proxy-api");

    public event Action? ConfigChanged;

    public ConfigManager()
    {
        _configDebouncer = new VibeProxy.Core.Utils.Debouncer(TimeSpan.FromMilliseconds(500));
    }

    public string GetMergedConfigPath(string baseConfigPath, SettingsStore settingsStore)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ConfigManager));

        _baseConfigPath = baseConfigPath;
        _settingsStore = settingsStore;

        Directory.CreateDirectory(AuthDirectory);

        var zaiKeys = LoadZaiKeys(AuthDirectory);
        var disabledProviders = BuildDisabledProviders(settingsStore);

        if (zaiKeys.Count == 0 && disabledProviders.Count == 0)
        {
            StartConfigWatcher();
            return baseConfigPath;
        }

        var bundledContent = File.Exists(baseConfigPath)
            ? File.ReadAllText(baseConfigPath)
            : string.Empty;

        var additional = new StringBuilder();

        if (disabledProviders.Count > 0)
        {
            additional.AppendLine();
            additional.AppendLine("# Provider exclusions (auto-added by VibeProxy)");
            additional.AppendLine("# Disabled providers have all models excluded");
            additional.AppendLine("oauth-excluded-models:");
            foreach (var provider in disabledProviders.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                additional.AppendLine($"  {provider}:");
                additional.AppendLine("    - \"*\"");
            }
        }

        if (zaiKeys.Count > 0 && settingsStore.IsProviderEnabled("zai"))
        {
            additional.AppendLine();
            additional.AppendLine("# Z.AI GLM Provider (auto-added by VibeProxy)");
            additional.AppendLine("openai-compatibility:");
            additional.AppendLine("  - name: \"zai\"");
            additional.AppendLine("    base-url: \"https://api.z.ai/api/coding/paas/v4\"");
            additional.AppendLine("    api-key-entries:");
            foreach (var key in zaiKeys)
            {
                var escaped = key
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
                additional.AppendLine($"      - api-key: \"{escaped}\"");
            }
            additional.AppendLine("    models:");
            additional.AppendLine("      - name: \"glm-4.7\"");
            additional.AppendLine("        alias: \"glm-4.7\"");
            additional.AppendLine("      - name: \"glm-4-plus\"");
            additional.AppendLine("        alias: \"glm-4-plus\"");
            additional.AppendLine("      - name: \"glm-4-air\"");
            additional.AppendLine("        alias: \"glm-4-air\"");
            additional.AppendLine("      - name: \"glm-4-flash\"");
            additional.AppendLine("        alias: \"glm-4-flash\"");
        }

        var mergedContent = bundledContent + additional;
        var mergedPath = Path.Combine(AuthDirectory, "merged-config.yaml");
        File.WriteAllText(mergedPath, mergedContent);
        
        StartConfigWatcher();
        
        return mergedPath;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        if (_configWatcher != null)
        {
            _configWatcher.EnableRaisingEvents = false;
            _configWatcher.Dispose();
            _configWatcher = null;
        }

        _configDebouncer?.Dispose();
    }

    private void StartConfigWatcher()
    {
        if (_isDisposed || _configWatcher != null) return;

        try
        {
            // Watch for Z.AI key changes
            _configWatcher = new FileSystemWatcher(AuthDirectory, "zai-*.json")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _configWatcher.Created += OnConfigFileChanged;
            _configWatcher.Changed += OnConfigFileChanged;
            _configWatcher.Deleted += OnConfigFileChanged;
            _configWatcher.Renamed += OnConfigFileChanged;
            _configWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start config watcher: {ex.Message}");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isDisposed) return;

        _configDebouncer.Execute(() =>
        {
            try
            {
                // Regenerate config when Z.AI keys change
                if (_baseConfigPath != null && _settingsStore != null)
                {
                    GetMergedConfigPath(_baseConfigPath, _settingsStore);
                    ConfigChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Config regeneration failed: {ex.Message}");
            }
        });
    }

    private static List<string> LoadZaiKeys(string authDir)
    {
        var keys = new List<string>();
        if (!Directory.Exists(authDir))
        {
            return keys;
        }

        foreach (var file in Directory.EnumerateFiles(authDir, "zai-*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("api_key", out var keyProp))
                {
                    var key = keyProp.GetString();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        keys.Add(key!);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return keys;
    }

    private static List<string> BuildDisabledProviders(SettingsStore settingsStore)
    {
        var disabled = new List<string>();
        foreach (var entry in OAuthProviderKeys)
        {
            if (!settingsStore.IsProviderEnabled(entry.Key))
            {
                disabled.Add(entry.Value);
            }
        }

        return disabled;
    }
}
