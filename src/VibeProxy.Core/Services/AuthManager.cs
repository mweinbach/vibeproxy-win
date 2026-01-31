using System.Text.Json;
using VibeProxy.Core.Models;

namespace VibeProxy.Core.Services;

public sealed class AuthManager
{
    private static readonly Dictionary<string, ServiceType> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = ServiceType.Claude,
        ["codex"] = ServiceType.Codex,
        ["github-copilot"] = ServiceType.Copilot,
        ["copilot"] = ServiceType.Copilot,
        ["gemini"] = ServiceType.Gemini,
        ["qwen"] = ServiceType.Qwen,
        ["antigravity"] = ServiceType.Antigravity,
        ["zai"] = ServiceType.Zai
    };

    public event Action<IReadOnlyDictionary<ServiceType, ServiceAccounts>>? AccountsUpdated;

    public IReadOnlyDictionary<ServiceType, ServiceAccounts> CurrentAccounts { get; private set; }
        = new Dictionary<ServiceType, ServiceAccounts>();

    public string AuthDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cli-proxy-api");

    public async Task RefreshAsync()
    {
        Directory.CreateDirectory(AuthDirectory);

        var accounts = new Dictionary<ServiceType, List<AuthAccount>>();
        foreach (var type in Enum.GetValues<ServiceType>())
        {
            accounts[type] = new List<AuthAccount>();
        }

        foreach (var file in Directory.EnumerateFiles(AuthDirectory, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("type", out var typeProp))
                {
                    continue;
                }

                var typeKey = typeProp.GetString() ?? string.Empty;
                if (!TypeMap.TryGetValue(typeKey, out var serviceType))
                {
                    continue;
                }

                var email = document.RootElement.TryGetProperty("email", out var emailProp)
                    ? emailProp.GetString()
                    : null;
                var login = document.RootElement.TryGetProperty("login", out var loginProp)
                    ? loginProp.GetString()
                    : null;

                DateTimeOffset? expired = null;
                if (document.RootElement.TryGetProperty("expired", out var expiredProp))
                {
                    var expiredString = expiredProp.GetString();
                    if (!string.IsNullOrWhiteSpace(expiredString))
                    {
                        if (DateTimeOffset.TryParse(expiredString, out var parsed))
                        {
                            expired = parsed;
                        }
                        else if (DateTimeOffset.TryParseExact(expiredString, "yyyy-MM-dd'T'HH:mm:ssK", null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedExact))
                        {
                            expired = parsedExact;
                        }
                    }
                }

                var account = new AuthAccount(
                    Path.GetFileName(file),
                    email,
                    login,
                    serviceType,
                    expired,
                    file);

                accounts[serviceType].Add(account);
            }
            catch
            {
                // Ignore malformed files
            }
        }

        var snapshot = accounts.ToDictionary(
            pair => pair.Key,
            pair => new ServiceAccounts(pair.Key, pair.Value));

        CurrentAccounts = snapshot;
        AccountsUpdated?.Invoke(CurrentAccounts);
    }

    public bool DeleteAccount(AuthAccount account)
    {
        try
        {
            File.Delete(account.FilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}