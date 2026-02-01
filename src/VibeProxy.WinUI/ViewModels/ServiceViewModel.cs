using System.Collections.ObjectModel;
using VibeProxy.Core.Models;
using VibeProxy.WinUI.Helpers;

namespace VibeProxy.WinUI.ViewModels;

public sealed class ServiceViewModel : ObservableObject
{
    private bool _isEnabled = true;
    private bool _isAuthenticating;
    private bool _isExpanded;
    private bool _isDisposed;

    public ServiceViewModel(ServiceType type, string iconUri, string? helpText)
    {
        Type = type;
        IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconUri));
        HelpText = helpText;
        Accounts = new ObservableCollection<AuthAccountViewModel>();
        ConnectCommand = new RelayCommand(() => OnConnectRequested());
        RemoveCommand = new RelayCommand<AuthAccountViewModel>(account => OnRemoveRequested(account));
    }

    public ServiceType Type { get; }
    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage IconSource { get; }
    public string? HelpText { get; }

    public string DisplayName => Type.DisplayName();

    public bool ShowVercelControls => Type == ServiceType.Claude;

    public ObservableCollection<AuthAccountViewModel> Accounts { get; }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand<AuthAccountViewModel> RemoveCommand { get; }

    public event Action<ServiceViewModel>? ConnectRequested;
    public event Action<ServiceViewModel, AuthAccountViewModel>? RemoveRequested;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                RaisePropertyChanged(nameof(IsDisabled));
            }
        }
    }

    public bool IsDisabled => !IsEnabled;

    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        set => SetProperty(ref _isAuthenticating, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool HasAccounts => Accounts.Count > 0;

    public string SummaryText
    {
        get
        {
            if (!HasAccounts)
            {
                return "No connected accounts";
            }

            var active = Accounts.Count(a => !a.IsExpired);
            if (Accounts.Count > 1)
            {
                return $"{Accounts.Count} connected accounts • Round-robin w/ auto-failover";
            }

            return active > 0 ? "1 connected account" : "1 connected account (expired)";
        }
    }

    public void RefreshAccounts(IEnumerable<AuthAccount> accounts)
    {
        if (_isDisposed) return;

        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(new AuthAccountViewModel(account, RemoveCommand));
        }

        if (Accounts.Any(a => a.IsExpired))
        {
            IsExpanded = true;
        }

        RaisePropertyChanged(nameof(HasAccounts));
        RaisePropertyChanged(nameof(SummaryText));
    }

    private void OnConnectRequested()
    {
        if (!_isDisposed)
        {
            ConnectRequested?.Invoke(this);
        }
    }

    private void OnRemoveRequested(AuthAccountViewModel? account)
    {
        if (!_isDisposed && account is not null)
        {
            RemoveRequested?.Invoke(this, account);
        }
    }
}
