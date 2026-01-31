using System.Text.Json;

namespace VibeProxy.Core.Services;

public sealed class SettingsStore
{
    private readonly string _settingsPath;
    private SettingsData _data;

    public event Action? SettingsChanged;
    public event Action? VercelConfigChanged;

    public SettingsStore()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeProxy");
        Directory.CreateDirectory(baseDir);
        _settingsPath = Path.Combine(baseDir, "settings.json");
        _data = Load();
    }

    public bool IsProviderEnabled(string providerKey)
    {
        if (_data.EnabledProviders.TryGetValue(providerKey, out var enabled))
        {
            return enabled;
        }

        return true;
    }

    public void SetProviderEnabled(string providerKey, bool enabled)
    {
        _data.EnabledProviders[providerKey] = enabled;
        Save();
        SettingsChanged?.Invoke();
    }

    public bool VercelGatewayEnabled
    {
        get => _data.VercelGatewayEnabled;
        set
        {
            if (_data.VercelGatewayEnabled == value)
            {
                return;
            }

            _data.VercelGatewayEnabled = value;
            Save();
            VercelConfigChanged?.Invoke();
        }
    }

    public string VercelApiKey
    {
        get => _data.VercelApiKey;
        set
        {
            if (string.Equals(_data.VercelApiKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _data.VercelApiKey = value;
            Save();
            VercelConfigChanged?.Invoke();
        }
    }

    public IReadOnlyDictionary<string, bool> EnabledProviders => _data.EnabledProviders;

    private SettingsData Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new SettingsData();
            }

            var json = File.ReadAllText(_settingsPath);
            var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
            var data = JsonSerializer.Deserialize<SettingsData>(json, options);
            return data ?? new SettingsData();
        }
        catch
        {
            return new SettingsData();
        }
    }

    private void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };
            var json = JsonSerializer.Serialize(_data, options);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Ignore settings save failures
        }
    }

    private sealed class SettingsData
    {
        public Dictionary<string, bool> EnabledProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool VercelGatewayEnabled { get; set; }
        public string VercelApiKey { get; set; } = string.Empty;
    }
}
