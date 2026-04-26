using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;

namespace VELO.App.Startup;

/// <summary>
/// Coordinates a single VELO instance per user. The first VELO process owns
/// a named mutex and a named-pipe listener; any later <c>VELO.exe URL</c>
/// invocation (e.g. Bambu Studio update window, default-browser handoff,
/// "open in browser" from another app) connects to the pipe, forwards the URL,
/// and exits. The owner opens the URL in a new tab and brings its window to
/// the front.
///
/// All names are scoped per Windows user (via SID) so two users on the same
/// machine each get their own VELO instance.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexBaseName = "VELO_Browser_SingleInstance";
    private const string PipeBaseName  = "VELO_Browser_Pipe";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _serverCts;

    public bool IsFirstInstance { get; }

    /// <summary>Fires on the UI thread of the owning instance when another VELO
    /// process forwards a URL to this one. The handler should open it in a new tab.</summary>
    public event Action<string>? UrlReceived;

    public SingleInstanceManager()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "global";
        _pipeName = $"{PipeBaseName}_{sid}";
        var mutexName = $"Local\\{MutexBaseName}_{sid}";

        _mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>Sends the given URL to the existing VELO instance. Returns true on success.</summary>
    public bool ForwardUrl(string url)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(2000); // 2-second timeout — owner should be alive
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(url);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] ForwardUrl failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Starts the named-pipe listener loop in the background. Owner-only.</summary>
    public void StartServer()
    {
        if (!IsFirstInstance) return;
        _serverCts = new CancellationTokenSource();
        _ = Task.Run(() => RunServerLoop(_serverCts.Token));
    }

    private async Task RunServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                var url = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var u = url; // capture for closure
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        new Action(() => UrlReceived?.Invoke(u)));
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[VELO] Pipe server loop error: {ex.Message}");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        try { _serverCts?.Cancel(); } catch { }
        try
        {
            if (IsFirstInstance) _mutex.ReleaseMutex();
        }
        catch { }
        _mutex.Dispose();
    }
}
