using System.Collections.ObjectModel;

namespace VELO.Core.Downloads;

public class DownloadManager
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public ObservableCollection<DownloadItem> Downloads { get; } = [];

    public event EventHandler<DownloadItem>? DownloadStarted;

    // v2.0.5.8 — Synchronous in-flight registry for dedup. The visible
    // ObservableCollection is updated via RunOnUi (async post), so checking
    // it for "is this URL already starting?" loses the race when two
    // DownloadStarting events fire back-to-back on the same click. Track
    // here synchronously under a lock so the second fire sees the first.
    private readonly Dictionary<string, (DownloadItem Item, DateTime StartedAt)> _inFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(2);

    public DownloadItem StartDownload(string url, string fileName, string filePath, long totalBytes)
    {
        // De-dupe rapid double fires of WebView2's DownloadStarting event.
        // Some pages (incl. our own landing) pair a <a download> anchor with
        // a JS click handler, so OnDownloadStarting fires twice for one click.
        // The dedup check + insertion happen inside the lock so the second
        // fire always sees the first, regardless of UI-thread posting.
        var now = DateTime.Now;

        lock (_lock)
        {
            // Evict expired in-flight entries (anything older than 5s — long
            // enough to outlive the dedup window without growing forever).
            var expired = _inFlight
                .Where(kv => (now - kv.Value.StartedAt) > TimeSpan.FromSeconds(5))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in expired) _inFlight.Remove(k);

            // Same URL within the dedup window? Return the original item —
            // both BytesReceivedChanged subscribers in BrowserTab end up
            // updating the same item, which is correct (single underlying op).
            if (_inFlight.TryGetValue(url, out var prev) && (now - prev.StartedAt) < DedupWindow)
                return prev.Item;

            var item = new DownloadItem
            {
                Url        = url,
                FileName   = fileName,
                FilePath   = filePath,
                TotalBytes = totalBytes,
                State      = DownloadState.InProgress,
                // StartedAt defaults to DateTime.Now in the constructor
            };
            _inFlight[url] = (item, now);

            RunOnUi(() => Downloads.Insert(0, item));
            DownloadStarted?.Invoke(this, item);
            return item;
        }
    }

    public void ClearCompleted()
    {
        var done = Downloads.Where(d => d.State != DownloadState.InProgress).ToList();
        RunOnUi(() => { foreach (var d in done) Downloads.Remove(d); });
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext != null)
            _uiContext.Post(_ => action(), null);
        else
            action();
    }
}
