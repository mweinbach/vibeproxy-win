using VibeProxy.Core.Models;
using VibeProxy.WinUI.Helpers;

namespace VibeProxy.WinUI.ViewModels;

public sealed class AuthAccountViewModel : ObservableObject
{
    public AuthAccountViewModel(AuthAccount account, RelayCommand<AuthAccountViewModel> removeCommand)
    {
        Account = account;
        RemoveCommand = removeCommand;
    }

    public AuthAccount Account { get; }
    public string DisplayName => Account.DisplayName;
    public bool IsExpired => Account.IsExpired;
    public RelayCommand<AuthAccountViewModel> RemoveCommand { get; }
}
