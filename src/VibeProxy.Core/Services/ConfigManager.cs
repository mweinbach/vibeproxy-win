using System.Text;
using System.Text.Json;

namespace VibeProxy.Core.Services;

public sealed class ConfigManager
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

    public string AuthDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cli-proxy-api");

    public string GetMergedConfigPath(string baseConfigPath, SettingsStore settingsStore)
    {
        Directory.CreateDirectory(AuthDirectory);

        var zaiKeys = LoadZaiKeys(AuthDirectory);
        var disabledProviders = BuildDisabledProviders(settingsStore);

        if (zaiKeys.Count == 0 && disabledProviders.Count == 0)
        {
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
        return mergedPath;
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