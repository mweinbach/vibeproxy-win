using System.Diagnostics;
using System.Net.Sockets;
using VibeProxy.Core.Utils;

namespace VibeProxy.Core.Services;

public sealed class ServerManager : IDisposable
{
    private const int DefaultProxyPort = 8317;
    private const int DefaultBackendPort = 8318;
    private const int HealthCheckIntervalMs = 5000;
    private const int MaxConsecutiveFailures = 3;
    private const int PortWaitTimeoutSeconds = 5;

    private readonly RingBuffer<string> _logBuffer = new(1000);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConfigManager _configManager;
    private readonly SettingsStore _settingsStore;
    private readonly ProxyConfigWriter _proxyConfigWriter;
    private readonly Timer _healthCheckTimer;

    private Process? _proxyProcess;
    private Process? _backendProcess;
    private string? _resourceBasePath;
    private int _consecutiveFailures;
    private bool _isDisposed;

    public event Action<bool>? RunningChanged;
    public event Action<IReadOnlyList<string>>? LogUpdated;

    public bool IsRunning { get; private set; }
    public int ProxyPort => DefaultProxyPort;
    public int BackendPort => DefaultBackendPort;

    public ServerManager(ConfigManager configManager, SettingsStore settingsStore, ProxyConfigWriter proxyConfigWriter)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _proxyConfigWriter = proxyConfigWriter ?? throw new ArgumentNullException(nameof(proxyConfigWriter));
        _healthCheckTimer = new Timer(OnHealthCheckTick, null, Timeout.Infinite, HealthCheckIntervalMs);
    }

    public void SetResourceBasePath(string basePath)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ServerManager));
        _resourceBasePath = basePath;
    }

    public string? ProxyExecutablePath => _resourceBasePath is null
        ? null
        : Path.Combine(_resourceBasePath, "Resources", "bin", "thinking-proxy.exe");

    public string? BackendExecutablePath => _resourceBasePath is null
        ? null
        : Path.Combine(_resourceBasePath, "Resources", "bin", "cli-proxy-api-plus.exe");

    public string? BaseConfigPath => _resourceBasePath is null
        ? null
        : Path.Combine(_resourceBasePath, "Resources", "config.yaml");

    public IReadOnlyList<string> Logs => _logBuffer.ToList();

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ServerManager));

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                AddLog("Server is already running");
                return true;
            }

            _consecutiveFailures = 0;

            // Validate prerequisites
            if (!ValidatePrerequisites())
            {
                return false;
            }

            // Clean up any orphaned processes
            await KillOrphanedProcessesAsync().ConfigureAwait(false);

            // Sync and generate configuration
            SyncProxyConfig();
            var mergedConfigPath = _configManager.GetMergedConfigPath(BaseConfigPath!, _settingsStore);

            if (!File.Exists(mergedConfigPath))
            {
                AddLog("❌ Could not create merged config");
                return false;
            }

            // Start proxy with retry
            if (!await StartProxyWithRetryAsync(cancellationToken).ConfigureAwait(false))
            {
                AddLog("❌ Failed to start thinking proxy after retries");
                return false;
            }

            // Wait for proxy port with extended timeout
            var proxyReady = await WaitForPortAsync(ProxyPort, TimeSpan.FromSeconds(PortWaitTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            if (!proxyReady)
            {
                AddLog($"❌ Thinking proxy failed to respond on port {ProxyPort}");
                await CleanupProcessesAsync().ConfigureAwait(false);
                return false;
            }

            // Start backend
            if (!await StartBackendAsync(mergedConfigPath, cancellationToken).ConfigureAwait(false))
            {
                AddLog("❌ Failed to start backend");
                await CleanupProcessesAsync().ConfigureAwait(false);
                return false;
            }

            // Verify backend port
            var backendReady = await WaitForPortAsync(BackendPort, TimeSpan.FromSeconds(PortWaitTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            if (!backendReady)
            {
                AddLog($"❌ Backend failed to respond on port {BackendPort}");
                await CleanupProcessesAsync().ConfigureAwait(false);
                return false;
            }

            // Start health monitoring
            _healthCheckTimer.Change(HealthCheckIntervalMs, HealthCheckIntervalMs);

            // Update state (outside lock to prevent reentrancy)
            var wasRunning = IsRunning;
            IsRunning = true;
            AddLog($"✓ Server started on port {ProxyPort}");
            
            // Fire event outside the lock
            if (!wasRunning)
            {
                _ = Task.Run(() => RunningChanged?.Invoke(true));
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            AddLog("❌ Server start cancelled");
            await CleanupProcessesAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Failed to start server: {ex.Message}");
            await CleanupProcessesAsync().ConfigureAwait(false);
            return false;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ServerManager));

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void SyncProxyConfig()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ServerManager));
        _proxyConfigWriter.Write(_settingsStore.VercelGatewayEnabled, _settingsStore.VercelApiKey);
    }

    public void RefreshConfig()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ServerManager));
        if (BaseConfigPath is null || !File.Exists(BaseConfigPath))
        {
            return;
        }

        _ = _configManager.GetMergedConfigPath(BaseConfigPath, _settingsStore);
    }

    public async Task<AuthResult> RunAuthCommandAsync(AuthCommand command, string? qwenEmail = null, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ServerManager));

        if (BackendExecutablePath is null || !File.Exists(BackendExecutablePath))
        {
            return new AuthResult(false, "Backend binary not found");
        }

        if (BaseConfigPath is null)
        {
            return new AuthResult(false, "Config not found");
        }

        var psi = new ProcessStartInfo
        {
            FileName = BackendExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(BackendExecutablePath) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(BaseConfigPath);

        switch (command)
        {
            case AuthCommand.ClaudeLogin:
                psi.ArgumentList.Add("-claude-login");
                break;
            case AuthCommand.CodexLogin:
                psi.ArgumentList.Add("-codex-login");
                break;
            case AuthCommand.CopilotLogin:
                psi.ArgumentList.Add("-github-copilot-login");
                break;
            case AuthCommand.GeminiLogin:
                psi.ArgumentList.Add("-login");
                break;
            case AuthCommand.QwenLogin:
                psi.ArgumentList.Add("-qwen-login");
                break;
            case AuthCommand.AntigravityLogin:
                psi.ArgumentList.Add("-antigravity-login");
                break;
        }

        var outputBuffer = new List<string>();
        var tcs = new TaskCompletionSource<bool>();
        
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    outputBuffer.Add(args.Data);
                }
            };
            
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    outputBuffer.Add(args.Data);
                }
            };

            process.Exited += (_, _) => tcs.TrySetResult(true);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            AddLog($"✓ Authentication process started (PID: {process.Id})");

            // Schedule input writes based on command type
            var inputTasks = ScheduleAuthInputAsync(process, command, qwenEmail, cancellationToken);

            // Wait for process to exit or timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout for auth

            try
            {
                await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout - process is still running
                try { process.Kill(entireProcessTree: true); } catch { }
                return new AuthResult(true, "Browser opened for authentication. Complete the login in your browser.");
            }

            var output = string.Join("\n", outputBuffer);
            
            if (output.Contains("Opening browser", StringComparison.OrdinalIgnoreCase) || process.ExitCode == 0)
            {
                if (command == AuthCommand.CopilotLogin)
                {
                    var code = ExtractCopilotCode(outputBuffer);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        return new AuthResult(true, 
                            $"🌐 Browser opened for GitHub authentication.\n\n📋 Code: {code}\n\nJust paste it in the browser!\n\nThe app will automatically detect when you're authenticated.", 
                            code);
                    }
                }

                return new AuthResult(true, "Browser opened for authentication. Complete the login in your browser.");
            }

            return new AuthResult(false, string.IsNullOrWhiteSpace(output) ? "Authentication failed" : output);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, $"Failed to start auth process: {ex.Message}");
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }

    public (bool ok, string message) SaveZaiApiKey(string apiKey)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ServerManager));

        try
        {
            var authDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cli-proxy-api");
            Directory.CreateDirectory(authDir);

            var keyPreview = string.Concat(apiKey.Take(8)) + "..." + string.Concat(apiKey.TakeLast(4));
            var timestamp = DateTimeOffset.UtcNow.ToString("o");
            var filename = $"zai-{Guid.NewGuid().ToString("N")[..8]}.json";
            var filePath = Path.Combine(authDir, filename);

            var authData = new Dictionary<string, object?>
            {
                ["type"] = "zai",
                ["email"] = keyPreview,
                ["api_key"] = apiKey,
                ["created"] = timestamp
            };

            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), 
                WriteIndented = true 
            };
            var json = System.Text.Json.JsonSerializer.Serialize(authData, options);
            File.WriteAllText(filePath, json);
            AddLog($"✓ Z.AI API key saved to {filename}");
            return (true, "API key saved successfully");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to save API key: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        _healthCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _healthCheckTimer?.Dispose();
        
        try
        {
            StopInternalAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort cleanup
        }
        
        _lifecycleLock?.Dispose();
    }

    private bool ValidatePrerequisites()
    {
        if (ProxyExecutablePath is null || !File.Exists(ProxyExecutablePath))
        {
            AddLog($"❌ Missing thinking proxy at {ProxyExecutablePath ?? "(null)"}");
            return false;
        }

        if (BackendExecutablePath is null || !File.Exists(BackendExecutablePath))
        {
            AddLog($"❌ Missing cli-proxy-api-plus at {BackendExecutablePath ?? "(null)"}");
            return false;
        }

        if (BaseConfigPath is null || !File.Exists(BaseConfigPath))
        {
            AddLog("❌ Missing config.yaml");
            return false;
        }

        return true;
    }

    private async Task<bool> StartProxyWithRetryAsync(CancellationToken cancellationToken, int maxRetries = 2)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                AddLog($"🔄 Retrying proxy start (attempt {attempt + 1}/{maxRetries + 1})...");
                await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
                await CleanupProxyAsync().ConfigureAwait(false);
            }

            if (await StartProxyAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> StartProxyAsync(CancellationToken cancellationToken)
    {
        if (ProxyExecutablePath is null) return false;

        var psi = new ProcessStartInfo
        {
            FileName = ProxyExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(ProxyExecutablePath) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("--listen");
        psi.ArgumentList.Add(ProxyPort.ToString());
        psi.ArgumentList.Add("--target");
        psi.ArgumentList.Add(BackendPort.ToString());
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(_proxyConfigWriter.ConfigPath);

        _proxyProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
        
        var tcs = new TaskCompletionSource<bool>();
        _proxyProcess.Exited += (_, _) => 
        {
            tcs.TrySetResult(false);
            OnProcessExited("proxy");
        };

        _proxyProcess.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                AddLog(args.Data);
            }
        };
        
        _proxyProcess.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                AddLog("⚠️ " + args.Data);
            }
        };

        try
        {
            _proxyProcess.Start();
            _proxyProcess.BeginOutputReadLine();
            _proxyProcess.BeginErrorReadLine();
            AddLog("✓ Thinking proxy process started");
            return true;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Failed to start thinking proxy: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> StartBackendAsync(string configPath, CancellationToken cancellationToken)
    {
        if (BackendExecutablePath is null) return false;

        var psi = new ProcessStartInfo
        {
            FileName = BackendExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(BackendExecutablePath) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-config");
        psi.ArgumentList.Add(configPath);

        _backendProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
        
        var tcs = new TaskCompletionSource<bool>();
        _backendProcess.Exited += (_, _) => 
        {
            tcs.TrySetResult(false);
            OnProcessExited("backend");
        };

        _backendProcess.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                AddLog(args.Data);
            }
        };
        
        _backendProcess.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                AddLog("⚠️ " + args.Data);
            }
        };

        try
        {
            _backendProcess.Start();
            _backendProcess.BeginOutputReadLine();
            _backendProcess.BeginErrorReadLine();
            AddLog($"✓ Backend process started on port {BackendPort}");
            return true;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Failed to start backend: {ex.Message}");
            return false;
        }
    }

    private async Task StopInternalAsync()
    {
        _healthCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        await CleanupProcessesAsync().ConfigureAwait(false);
        
        var wasRunning = IsRunning;
        IsRunning = false;
        AddLog("✓ Server stopped");
        
        if (wasRunning)
        {
            _ = Task.Run(() => RunningChanged?.Invoke(false));
        }
    }

    private async Task CleanupProcessesAsync()
    {
        await CleanupProxyAsync().ConfigureAwait(false);
        await CleanupBackendAsync().ConfigureAwait(false);
    }

    private async Task CleanupProxyAsync()
    {
        if (_proxyProcess is null) return;

        try
        {
            if (!_proxyProcess.HasExited)
            {
                _proxyProcess.Kill(entireProcessTree: true);
                await Task.WhenAny(
                    Task.Run(() => _proxyProcess.WaitForExit(2000)),
                    Task.Delay(2500)
                ).ConfigureAwait(false);
            }
        }
        catch { }
        finally
        {
            try { _proxyProcess.Dispose(); } catch { }
            _proxyProcess = null;
        }
    }

    private async Task CleanupBackendAsync()
    {
        if (_backendProcess is null) return;

        try
        {
            if (!_backendProcess.HasExited)
            {
                _backendProcess.Kill(entireProcessTree: true);
                await Task.WhenAny(
                    Task.Run(() => _backendProcess.WaitForExit(2000)),
                    Task.Delay(2500)
                ).ConfigureAwait(false);
            }
        }
        catch { }
        finally
        {
            try { _backendProcess.Dispose(); } catch { }
            _backendProcess = null;
        }
    }

    private async Task KillOrphanedProcessesAsync()
    {
        foreach (var name in new[] { "cli-proxy-api-plus", "thinking-proxy" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await Task.Run(() => process.WaitForExit(2000)).ConfigureAwait(false);
                }
                catch { }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }
    }

    private void OnHealthCheckTick(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _lifecycleLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!IsRunning || _isDisposed) return;

                    var proxyHealthy = IsProcessHealthy(_proxyProcess) && await IsPortRespondingAsync(ProxyPort).ConfigureAwait(false);
                    var backendHealthy = IsProcessHealthy(_backendProcess) && await IsPortRespondingAsync(BackendPort).ConfigureAwait(false);

                    if (!proxyHealthy || !backendHealthy)
                    {
                        _consecutiveFailures++;
                        AddLog($"⚠️ Health check failed (attempt {_consecutiveFailures}/{MaxConsecutiveFailures})");

                        if (_consecutiveFailures >= MaxConsecutiveFailures)
                        {
                            AddLog("❌ Max health check failures reached, restarting server...");
                            await StopInternalAsync().ConfigureAwait(false);
                            _lifecycleLock.Release();
                            
                            // Try to restart outside the lock
                            await Task.Delay(1000).ConfigureAwait(false);
                            await StartAsync().ConfigureAwait(false);
                            return;
                        }
                    }
                    else
                    {
                        if (_consecutiveFailures > 0)
                        {
                            AddLog("✓ Health check passed, server is healthy");
                            _consecutiveFailures = 0;
                        }
                    }
                }
                finally
                {
                    if (_lifecycleLock.CurrentCount == 0)
                    {
                        _lifecycleLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Health check error: {ex.Message}");
            }
        });
    }

    private void OnProcessExited(string processType)
    {
        if (!IsRunning || _isDisposed) return;

        AddLog($"⚠️ {processType} process exited unexpectedly");
        _consecutiveFailures = MaxConsecutiveFailures; // Force restart on next health check
    }

    private static bool IsProcessHealthy(Process? process)
    {
        if (process is null) return false;
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsPortRespondingAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private void AddLog(string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("T");
        _logBuffer.Append($"[{timestamp}] {message}");
        
        // Fire event outside any lock
        _ = Task.Run(() => LogUpdated?.Invoke(_logBuffer.ToList()));
    }

    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(100, cancellationToken)).ConfigureAwait(false);
                if (completed == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch
            {
                // retry
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static List<Task> ScheduleAuthInputAsync(Process process, AuthCommand command, string? qwenEmail, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        if (command == AuthCommand.GeminiLogin)
        {
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                if (!process.HasExited)
                {
                    await process.StandardInput.WriteLineAsync().ConfigureAwait(false);
                }
            }, cancellationToken));
        }

        if (command == AuthCommand.CodexLogin)
        {
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
                if (!process.HasExited)
                {
                    await process.StandardInput.WriteLineAsync().ConfigureAwait(false);
                }
            }, cancellationToken));
        }

        if (command == AuthCommand.QwenLogin && !string.IsNullOrWhiteSpace(qwenEmail))
        {
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                if (!process.HasExited)
                {
                    await process.StandardInput.WriteLineAsync(qwenEmail!).ConfigureAwait(false);
                }
            }, cancellationToken));
        }

        return tasks;
    }

    private static string? ExtractCopilotCode(IEnumerable<string> output)
    {
        foreach (var line in output)
        {
            var index = line.IndexOf("enter the code:", StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return line[(index + "enter the code:".Length)..].Trim();
            }
        }

        return null;
    }
}
