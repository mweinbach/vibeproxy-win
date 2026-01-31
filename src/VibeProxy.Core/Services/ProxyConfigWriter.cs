using System.Text.Json;

namespace VibeProxy.Core.Services;

public sealed class ProxyConfigWriter
{
    public string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cli-proxy-api", "proxy-config.json");

    public void Write(bool vercelEnabled, string vercelApiKey)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var payload = new ProxyConfig
        {
            VercelEnabled = vercelEnabled,
            VercelApiKey = vercelApiKey ?? string.Empty
        };

        var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };
        var json = JsonSerializer.Serialize(payload, options);
        File.WriteAllText(ConfigPath, json);
    }

    private sealed class ProxyConfig
    {
        public bool VercelEnabled { get; set; }
        public string VercelApiKey { get; set; } = string.Empty;
    }
}
