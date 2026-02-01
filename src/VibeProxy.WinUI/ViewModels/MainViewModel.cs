using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml;
using VibeProxy.Core.Models;
using VibeProxy.Core.Services;
using VibeProxy.WinUI.Helpers;

namespace VibeProxy.WinUI.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly AuthManager _authManager;
    private readonly ServerManager _serverManager;
    private readonly VibeProxy.Core.Utils.Debouncer _authDebouncer;
    private FileSystemWatcher? _authWatcher;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private CancellationTokenSource? _refreshCts;
    private bool _isDisposed;

    private bool _isServerRunning;

    public MainViewModel(SettingsStore settingsStore, AuthManager authManager, ServerManager serverManager)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _authDebouncer = new VibeProxy.Core.Utils.Debouncer(TimeSpan.FromMilliseconds(500));
        _refreshCts = new CancellationTokenSource();

        Services = new ObservableCollection<ServiceViewModel>(CreateServices());

        foreach (var service in Services)
        {
            service.IsEnabled = _settingsStore.IsProviderEnabled(ServiceKey(service.Type));
            service.ConnectRequested += OnConnectRequested;
            service.RemoveRequested += OnRemoveRequested;
            service.PropertyChanged += OnServicePropertyChanged;
        }

        _authManager.AccountsUpdated += OnAccountsUpdated;
        _serverManager.RunningChanged += OnRunningChanged;

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

    public void UpdateIconTheme(ElementTheme theme)
    {
        foreach (var service in Services)
        {
            service.UpdateIconTheme(theme);
        }
    }

    public event Func<ServiceViewModel, Task>? ConnectFlowRequested;
    public event Func<AuthAccountViewModel, Task>? RemoveFlowRequested;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MainViewModel));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _refreshCts!.Token);
        try
        {
            await RefreshAuthAsync(cts.Token).ConfigureAwait(false);
            StartAuthWatcher();
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose
        }
    }

    public void AttachDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MainViewModel));
        _dispatcher = dispatcher;
    }

    public async Task RefreshAuthAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return;

        try
        {
            await _authManager.RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error but don't crash
            System.Diagnostics.Debug.WriteLine($"Failed to refresh auth: {ex.Message}");
        }
    }

    public async Task<AuthResult> StartAuthAsync(ServiceType serviceType, string? qwenEmail = null, string? zaiApiKey = null, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MainViewModel));

        var service = Services.FirstOrDefault(s => s.Type == serviceType);
        if (service is null)
        {
            return new AuthResult(false, "Service not found");
        }

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
                await RefreshAuthAsync(cancellationToken).ConfigureAwait(false);
                
                // Restart server to pick up new API key
                if (_serverManager.IsRunning)
                {
                    await _serverManager.StopAsync(cancellationToken).ConfigureAwait(false);
                    await _serverManager.StartAsync(cancellationToken).ConfigureAwait(false);
                }
                
                return new AuthResult(result.ok, result.message);
            }

            if (serviceType == ServiceType.Qwen)
            {
                var result = await _serverManager.RunAuthCommandAsync(AuthCommand.QwenLogin, qwenEmail, cancellationToken).ConfigureAwait(false);
                await RefreshAuthAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }

            var command = serviceType switch
            {
                ServiceType.Claude => AuthCommand.ClaudeLogin,
                ServiceType.Codex => AuthCommand.CodexLogin,
                ServiceType.Copilot => AuthCommand.CopilotLogin,
                ServiceType.Gemini => AuthCommand.GeminiLogin,
                ServiceType.Antigravity => AuthCommand.AntigravityLogin,
                _ => throw new ArgumentException($"Unknown service type: {serviceType}")
            };

            var authResult = await _serverManager.RunAuthCommandAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
            await RefreshAuthAsync(cancellationToken).ConfigureAwait(false);
            return authResult;
        }
        finally
        {
            service.IsAuthenticating = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        // Cancel any pending operations
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        
        // Stop watching auth files
        if (_authWatcher != null)
        {
            _authWatcher.EnableRaisingEvents = false;
            _authWatcher.Dispose();
            _authWatcher = null;
        }

        // Unsubscribe from events to prevent memory leaks
        _authManager.AccountsUpdated -= OnAccountsUpdated;
        _serverManager.RunningChanged -= OnRunningChanged;

        foreach (var service in Services)
        {
            service.ConnectRequested -= OnConnectRequested;
            service.RemoveRequested -= OnRemoveRequested;
            service.PropertyChanged -= OnServicePropertyChanged;
        }

        // Dispose the debouncer
        _authDebouncer?.Dispose();
    }

    private void OnAccountsUpdated(IReadOnlyDictionary<ServiceType, ServiceAccounts> accounts)
    {
        if (_isDisposed) return;
        
        RunOnUI(() =>
        {
            if (_isDisposed) return;
            
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

    private void OnRunningChanged(bool running)
    {
        if (_isDisposed) return;
        RunOnUI(() => IsServerRunning = running);
    }

    private void OnServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_isDisposed) return;
        
        if (args.PropertyName == nameof(ServiceViewModel.IsEnabled) && sender is ServiceViewModel service)
        {
            _settingsStore.SetProviderEnabled(ServiceKey(service.Type), service.IsEnabled);
            _serverManager.RefreshConfig();
            
            // Properly await the refresh with error handling
            _ = RefreshAuthWithErrorHandlingAsync();
        }
    }

    private async Task RefreshAuthWithErrorHandlingAsync()
    {
        try
        {
            await RefreshAuthAsync(_refreshCts?.Token ?? default).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Debounced refresh failed: {ex.Message}");
        }
    }

    private void StartAuthWatcher()
    {
        if (_isDisposed || _authWatcher is not null) return;

        try
        {
            Directory.CreateDirectory(_authManager.AuthDirectory);
            _authWatcher = new FileSystemWatcher(_authManager.AuthDirectory, "*.json")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _authWatcher.Changed += OnAuthFileChanged;
            _authWatcher.Created += OnAuthFileChanged;
            _authWatcher.Deleted += OnAuthFileChanged;
            _authWatcher.Renamed += OnAuthFileChanged;
            _authWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start auth watcher: {ex.Message}");
        }
    }

    private void OnAuthFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isDisposed) return;
        
        _authDebouncer.Execute(async () =>
        {
            try
            {
                await RefreshAuthAsync(_refreshCts?.Token ?? default).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auth refresh failed: {ex.Message}");
            }
        });
    }

    private void RunOnUI(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcher.TryEnqueue(() => 
        {
            if (!_isDisposed)
            {
                action();
            }
        });
    }

    private static IEnumerable<ServiceViewModel> CreateServices()
    {
        yield return new ServiceViewModel(ServiceType.Antigravity,
            "ms-appx:///Assets/Icons/icon-antigravity-light.png",
            "ms-appx:///Assets/Icons/icon-antigravity-dark.png",
            "Antigravity provides OAuth-based access to various AI models including Gemini and Claude.");
        yield return new ServiceViewModel(ServiceType.Claude,
            "ms-appx:///Assets/Icons/icon-claude-light.png",
            "ms-appx:///Assets/Icons/icon-claude-dark.png",
            null);
        yield return new ServiceViewModel(ServiceType.Codex,
            "ms-appx:///Assets/Icons/icon-codex-light.png",
            "ms-appx:///Assets/Icons/icon-codex-dark.png",
            null);
        yield return new ServiceViewModel(ServiceType.Gemini,
            "ms-appx:///Assets/Icons/icon-gemini-light.png",
            "ms-appx:///Assets/Icons/icon-gemini-dark.png",
            "If you have multiple projects, the default project will be used.");
        yield return new ServiceViewModel(ServiceType.Copilot,
            "ms-appx:///Assets/Icons/icon-copilot-light.png",
            "ms-appx:///Assets/Icons/icon-copilot-dark.png",
            "GitHub Copilot provides access to Claude, GPT, Gemini and other models.");
        yield return new ServiceViewModel(ServiceType.Qwen,
            "ms-appx:///Assets/Icons/icon-qwen-light.png",
            "ms-appx:///Assets/Icons/icon-qwen-dark.png",
            null);
        yield return new ServiceViewModel(ServiceType.Zai,
            "ms-appx:///Assets/Icons/icon-zai-light.png",
            "ms-appx:///Assets/Icons/icon-zai-dark.png",
            "Z.AI GLM provides access to GLM-4.7 via API key.");
    }

    private static string ServiceKey(ServiceType type) => type switch
    {
        ServiceType.Copilot => "github-copilot",
        _ => type.ToString().ToLowerInvariant()
    };

    private async void OnConnectRequested(ServiceViewModel service)
    {
        if (_isDisposed || ConnectFlowRequested is null) return;

        try
        {
            await ConnectFlowRequested.Invoke(service).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Connect flow failed: {ex.Message}");
        }
    }

    private async void OnRemoveRequested(ServiceViewModel service, AuthAccountViewModel account)
    {
        if (_isDisposed || RemoveFlowRequested is null) return;

        try
        {
            await RemoveFlowRequested.Invoke(account).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Remove flow failed: {ex.Message}");
        }
    }
}
