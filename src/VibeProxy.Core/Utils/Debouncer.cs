namespace VibeProxy.Core.Utils;

public sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    public Debouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public void Execute(Action action)
    {
        if (_isDisposed) return;

        lock (_lock)
        {
            if (_isDisposed) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_delay, token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested)
                    {
                        action();
                    }
                }
                catch (TaskCanceledException)
                {
                    // ignored
                }
            }, token);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
