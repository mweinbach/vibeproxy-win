namespace VibeProxy.Core.Models;

public class ServiceAccounts
{
    public ServiceAccounts(ServiceType type, IReadOnlyList<AuthAccount> accounts)
    {
        Type = type;
        Accounts = accounts;
    }

    public ServiceType Type { get; }
    public IReadOnlyList<AuthAccount> Accounts { get; }

    public bool HasAccounts => Accounts.Count > 0;
    public int ActiveCount => Accounts.Count(a => !a.IsExpired);
    public int ExpiredCount => Accounts.Count(a => a.IsExpired);
}