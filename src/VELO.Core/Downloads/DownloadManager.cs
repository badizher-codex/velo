using System.Collections.ObjectModel;

namespace VELO.Core.Downloads;

public class DownloadManager
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public ObservableCollection<DownloadItem> Downloads { get; } = [];

    public event EventHandler<DownloadItem>? DownloadStarted;

    public DownloadItem StartDownload(string url, string fileName, string filePath, long totalBytes)
    {
        // v2.0.5.7 — De-dupe rapid double fires of WebView2's DownloadStarting
        // event. Some pages (incl. our own landing) couple a download anchor
        // with a JS click handler, so OnDownloadStarting can fire twice for
        // what the user experiences as one click. If we already have an
        // InProgress download for the same URL queued within the last 2s,
        // return that item and skip re-adding to the visible list.
        // DownloadItem.StartedAt defaults to DateTime.Now (local), so compare in the same clock.
        var now = DateTime.Now;
        var existing = Downloads.FirstOrDefault(d =>
            d.State == DownloadState.InProgress
            && string.Equals(d.Url, url, StringComparison.OrdinalIgnoreCase)
            && (now - d.StartedAt) < TimeSpan.FromSeconds(2));

        if (existing != null) return existing;

        var item = new DownloadItem
        {
            Url        = url,
            FileName   = fileName,
            FilePath   = filePath,
            TotalBytes = totalBytes,
            State      = DownloadState.InProgress,
            // StartedAt defaults to DateTime.Now in the constructor
        };

        RunOnUi(() => Downloads.Insert(0, item));
        DownloadStarted?.Invoke(this, item);
        return item;
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
