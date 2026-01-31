using System.Collections.ObjectModel;
using System.IO;
using VibeProxy.Core.Models;
using VibeProxy.Core.Services;
using VibeProxy.WinUI.Helpers;

namespace VibeProxy.WinUI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly AuthManager _authManager;
    private readonly ServerManager _serverManager;
    private readonly VibeProxy.Core.Utils.Debouncer _authDebouncer = new(TimeSpan.FromMilliseconds(500));
    private FileSystemWatcher? _authWatcher;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    private bool _isServerRunning;

    public MainViewModel(SettingsStore settingsStore, AuthManager authManager, ServerManager serverManager)
    {
        _settingsStore = settingsStore;
        _authManager = authManager;
        _serverManager = serverManager;

        Services = new ObservableCollection<ServiceViewModel>(CreateServices());

        foreach (var service in Services)
        {
            service.IsEnabled = _settingsStore.IsProviderEnabled(ServiceKey(service.Type));
            service.ConnectRequested += OnConnectRequested;
            service.RemoveRequested += OnRemoveRequested;
            service.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ServiceViewModel.IsEnabled))
                {
                    _settingsStore.SetProviderEnabled(ServiceKey(service.Type), service.IsEnabled);
                    _serverManager.RefreshConfig();
                    _ = RefreshAuthAsync();
                }
            };
        }

        _authManager.AccountsUpdated += UpdateAccounts;
        _serverManager.RunningChanged += running =>
        {
            RunOnUI(() => IsServerRunning = running);
        };

        IsServerRunning = _serverManager.IsRunning;
    }

    public ObservableCollection<ServiceViewModel> Services { get; }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        private set
        {
            if (SetProperty(ref _isServerRunning, value))
            {
                RaisePropertyChanged(nameof(ServerStatusText));
            }
        }
    }

    public string ServerStatusText => IsServerRunning ? "Running" : "Stopped";

    public SettingsStore SettingsStore => _settingsStore;

    public event Func<ServiceViewModel, Task>? ConnectFlowRequested;
    public event Func<AuthAccountViewModel, Task>? RemoveFlowRequested;

    public async Task InitializeAsync()
    {
        await RefreshAuthAsync().ConfigureAwait(false);
        StartAuthWatcher();
    }

    public void AttachDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task RefreshAuthAsync()
    {
        await _authManager.RefreshAsync().ConfigureAwait(false);
    }

    public async Task<AuthResult> StartAuthAsync(ServiceType serviceType, string? qwenEmail = null, string? zaiApiKey = null)
    {
        var service = Services.First(s => s.Type == serviceType);
        service.IsAuthenticating = true;

        try
        {
            if (serviceType == ServiceType.Zai)
            {
                if (string.IsNullOrWhiteSpace(zaiApiKey))
                {
                    return new AuthResult(false, "Missing API key");
                }

                var result = _serverManager.SaveZaiApiKey(zaiApiKey);
                await _authManager.RefreshAsync().ConfigureAwait(false);
                if (_serverManager.IsRunning)
                {
                    await _serverManager.StopAsync().ConfigureAwait(false);
                    await _serverManager.StartAsync().ConfigureAwait(false);
                }
                return new AuthResult(result.ok, result.message);
            }

            if (serviceType == ServiceType.Qwen)
            {
                var result = await _serverManager.RunAuthCommandAsync(AuthCommand.QwenLogin, qwenEmail).ConfigureAwait(false);
                await _authManager.RefreshAsync().ConfigureAwait(false);
                return result;
            }

            var command = serviceType switch
            {
                ServiceType.Claude => AuthCommand.ClaudeLogin,
                ServiceType.Codex => AuthCommand.CodexLogin,
                ServiceType.Copilot => AuthCommand.CopilotLogin,
                ServiceType.Gemini => AuthCommand.GeminiLogin,
                ServiceType.Antigravity => AuthCommand.AntigravityLogin,
                _ => AuthCommand.ClaudeLogin
            };

            var authResult = await _serverManager.RunAuthCommandAsync(command).ConfigureAwait(false);
            await _authManager.RefreshAsync().ConfigureAwait(false);
            return authResult;
        }
        finally
        {
            service.IsAuthenticating = false;
        }
    }

    private void UpdateAccounts(IReadOnlyDictionary<ServiceType, ServiceAccounts> accounts)
    {
        RunOnUI(() =>
        {
            foreach (var service in Services)
            {
                if (accounts.TryGetValue(service.Type, out var serviceAccounts))
                {
                    service.RefreshAccounts(serviceAccounts.Accounts);
                }
                else
                {
                    service.RefreshAccounts(Array.Empty<AuthAccount>());
                }
            }
        });
    }

    private void StartAuthWatcher()
    {
        if (_authWatcher is not null)
        {
            return;
        }

        Directory.CreateDirectory(_authManager.AuthDirectory);
        _authWatcher = new FileSystemWatcher(_authManager.AuthDirectory, "*.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _authWatcher.Changed += (_, _) => DebouncedRefresh();
        _authWatcher.Created += (_, _) => DebouncedRefresh();
        _authWatcher.Deleted += (_, _) => DebouncedRefresh();
        _authWatcher.Renamed += (_, _) => DebouncedRefresh();
        _authWatcher.EnableRaisingEvents = true;
    }

    private void DebouncedRefresh()
    {
        _authDebouncer.Execute(() => _ = RefreshAuthAsync());
    }

    private void RunOnUI(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcher.TryEnqueue(() => action());
    }

    private IEnumerable<ServiceViewModel> CreateServices()
    {
        yield return new ServiceViewModel(ServiceType.Antigravity, "ms-appx:///Assets/Icons/icon-antigravity.png",
            "Antigravity provides OAuth-based access to various AI models including Gemini and Claude.");
        yield return new ServiceViewModel(ServiceType.Claude, "ms-appx:///Assets/Icons/icon-claude.png", null);
        yield return new ServiceViewModel(ServiceType.Codex, "ms-appx:///Assets/Icons/icon-codex.png", null);
        yield return new ServiceViewModel(ServiceType.Gemini, "ms-appx:///Assets/Icons/icon-gemini.png",
            "If you have multiple projects, the default project will be used.");
        yield return new ServiceViewModel(ServiceType.Copilot, "ms-appx:///Assets/Icons/icon-copilot.png",
            "GitHub Copilot provides access to Claude, GPT, Gemini and other models.");
        yield return new ServiceViewModel(ServiceType.Qwen, "ms-appx:///Assets/Icons/icon-qwen.png", null);
        yield return new ServiceViewModel(ServiceType.Zai, "ms-appx:///Assets/Icons/icon-zai.png",
            "Z.AI GLM provides access to GLM-4.7 via API key.");
    }

    private static string ServiceKey(ServiceType type) => type switch
    {
        ServiceType.Copilot => "github-copilot",
        _ => type.ToString().ToLowerInvariant()
    };

    private void OnConnectRequested(ServiceViewModel service)
    {
        if (ConnectFlowRequested is null)
        {
            return;
        }

        _ = ConnectFlowRequested.Invoke(service);
    }

    private void OnRemoveRequested(ServiceViewModel service, AuthAccountViewModel account)
    {
        if (RemoveFlowRequested is null)
        {
            return;
        }

        _ = RemoveFlowRequested.Invoke(account);
    }
}
