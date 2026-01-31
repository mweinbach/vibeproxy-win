namespace VibeProxy.Core.Models;

public record AuthAccount(
    string Id,
    string? Email,
    string? Login,
    ServiceType Type,
    DateTimeOffset? Expired,
    string FilePath)
{
    public bool IsExpired => Expired.HasValue && Expired.Value < DateTimeOffset.UtcNow;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Email))
            {
                return Email!;
            }

            if (!string.IsNullOrWhiteSpace(Login))
            {
                return Login!;
            }

            return Id;
        }
    }
}