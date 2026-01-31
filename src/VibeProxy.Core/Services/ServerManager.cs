using System.Diagnostics;
using System.Net.Sockets;
using VibeProxy.Core.Utils;

namespace VibeProxy.Core.Services;

public sealed class ServerManager
{
    private const int DefaultProxyPort = 8317;
    private const int DefaultBackendPort = 8318;

    private readonly RingBuffer<string> _logBuffer = new(1000);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConfigManager _configManager;
    private readonly SettingsStore _settingsStore;
    private readonly ProxyConfigWriter _proxyConfigWriter;

    private Process? _proxyProcess;
    private Process? _backendProcess;
    private string? _resourceBasePath;

    public event Action<bool>? RunningChanged;
    public event Action<IReadOnlyList<string>>? LogUpdated;

    public bool IsRunning { get; private set; }
    public int ProxyPort => DefaultProxyPort;
    public int BackendPort => DefaultBackendPort;

    public ServerManager(ConfigManager configManager, SettingsStore settingsStore, ProxyConfigWriter proxyConfigWriter)
    {
        _configManager = configManager;
        _settingsStore = settingsStore;
        _proxyConfigWriter = proxyConfigWriter;
    }

    public void SetResourceBasePath(string basePath)
    {
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

    public async Task<bool> StartAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return true;
            }

            KillOrphanedProcesses();

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

            SyncProxyConfig();
            var mergedConfigPath = _configManager.GetMergedConfigPath(BaseConfigPath, _settingsStore);

            if (!File.Exists(mergedConfigPath))
            {
                AddLog("❌ Could not create merged config");
                return false;
            }

            if (!StartProxy())
            {
                return false;
            }

            var proxyReady = await WaitForPortAsync(ProxyPort, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (!proxyReady)
            {
                AddLog("❌ Thinking proxy failed to start");
                StopProxy();
                return false;
            }

            if (!StartBackend(mergedConfigPath))
            {
                StopProxy();
                return false;
            }

            IsRunning = true;
            AddLog($"✓ Server started on port {ProxyPort}");
            RunningChanged?.Invoke(true);
            return true;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            StopBackend();
            StopProxy();
            IsRunning = false;
            AddLog("✓ Server stopped");
            RunningChanged?.Invoke(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void SyncProxyConfig()
    {
        _proxyConfigWriter.Write(_settingsStore.VercelGatewayEnabled, _settingsStore.VercelApiKey);
    }

    public void RefreshConfig()
    {
        if (BaseConfigPath is null || !File.Exists(BaseConfigPath))
        {
            return;
        }

        _ = _configManager.GetMergedConfigPath(BaseConfigPath, _settingsStore);
    }

    public async Task<AuthResult> RunAuthCommandAsync(AuthCommand command, string? qwenEmail = null)
    {
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

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            AddLog($"✓ Authentication process started (PID: {process.Id})");

            if (command == AuthCommand.GeminiLogin)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    if (!process.HasExited)
                    {
                        await process.StandardInput.WriteLineAsync().ConfigureAwait(false);
                    }
                });
            }

            if (command == AuthCommand.CodexLogin)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                    if (!process.HasExited)
                    {
                        await process.StandardInput.WriteLineAsync().ConfigureAwait(false);
                    }
                });
            }

            if (command == AuthCommand.QwenLogin && !string.IsNullOrWhiteSpace(qwenEmail))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (!process.HasExited)
                    {
                        await process.StandardInput.WriteLineAsync(qwenEmail!).ConfigureAwait(false);
                    }
                });
            }

            if (command == AuthCommand.CopilotLogin)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }

            if (process.HasExited)
            {
                var output = string.Join("\n", outputBuffer);
                if (output.Contains("Opening browser", StringComparison.OrdinalIgnoreCase))
                {
                    return new AuthResult(true, "Browser opened for authentication. Complete the login in your browser.");
                }

                return process.ExitCode == 0
                    ? new AuthResult(true, "Browser opened for authentication. Complete the login in your browser.")
                    : new AuthResult(false, string.IsNullOrWhiteSpace(output) ? "Authentication failed" : output);
            }

            if (command == AuthCommand.CopilotLogin)
            {
                var code = ExtractCopilotCode(outputBuffer);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return new AuthResult(true, $"🌐 Browser opened for GitHub authentication.\n\n📋 Code: {code}\n\nJust paste it in the browser!\n\nThe app will automatically detect when you're authenticated.", code);
                }
            }

            return new AuthResult(true, "Browser opened for authentication. Complete the login in your browser.");
        }
        catch (Exception ex)
        {
            return new AuthResult(false, $"Failed to start auth process: {ex.Message}");
        }
    }

    public (bool ok, string message) SaveZaiApiKey(string apiKey)
    {
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

            var json = System.Text.Json.JsonSerializer.Serialize(authData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(filePath, json);
            AddLog($"✓ Z.AI API key saved to {filename}");
            return (true, "API key saved successfully");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to save API key: {ex.Message}");
        }
    }

    private bool StartProxy()
    {
        if (ProxyExecutablePath is null)
        {
            return false;
        }

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
        _proxyProcess.Exited += (_, _) =>
        {
            AddLog("Thinking proxy stopped.");
        };

        try
        {
            _proxyProcess.Start();
            _proxyProcess.BeginOutputReadLine();
            _proxyProcess.BeginErrorReadLine();
            AddLog("✓ Thinking proxy started");
            return true;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Failed to start thinking proxy: {ex.Message}");
            return false;
        }
    }

    private bool StartBackend(string configPath)
    {
        if (BackendExecutablePath is null)
        {
            return false;
        }

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
        _backendProcess.Exited += (_, _) =>
        {
            AddLog("Backend stopped.");
            IsRunning = false;
            RunningChanged?.Invoke(false);
        };

        try
        {
            _backendProcess.Start();
            _backendProcess.BeginOutputReadLine();
            _backendProcess.BeginErrorReadLine();
            AddLog($"✓ Backend started on port {BackendPort}");
            return true;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Failed to start backend: {ex.Message}");
            return false;
        }
    }

    private void StopProxy()
    {
        if (_proxyProcess is null)
        {
            return;
        }

        try
        {
            if (!_proxyProcess.HasExited)
            {
                _proxyProcess.Kill(entireProcessTree: true);
                _proxyProcess.WaitForExit(2000);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _proxyProcess.Dispose();
            _proxyProcess = null;
        }
    }

    private void StopBackend()
    {
        if (_backendProcess is null)
        {
            return;
        }

        try
        {
            if (!_backendProcess.HasExited)
            {
                _backendProcess.Kill(entireProcessTree: true);
                _backendProcess.WaitForExit(2000);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _backendProcess.Dispose();
            _backendProcess = null;
        }
    }

    private void KillOrphanedProcesses()
    {
        foreach (var name in new[] { "cli-proxy-api-plus", "thinking-proxy" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private void AddLog(string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("T");
        _logBuffer.Append($"[{timestamp}] {message}");
        LogUpdated?.Invoke(_logBuffer.ToList());
    }

    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(100)).ConfigureAwait(false);
                if (completed == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch
            {
                // retry
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return false;
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
