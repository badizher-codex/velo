namespace VELO.UI.Utilities;

/// <summary>
/// Debounces an async action: repeated calls within the delay window
/// cancel the previous pending invocation and restart the timer.
/// </summary>
public sealed class DebouncedAction(TimeSpan delay)
{
    private readonly TimeSpan _delay = delay;
    private CancellationTokenSource? _cts;

    public void Invoke(Func<CancellationToken, Task> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                    await action(cts.Token);
            }
            catch (OperationCanceledException) { }
        }, cts.Token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
