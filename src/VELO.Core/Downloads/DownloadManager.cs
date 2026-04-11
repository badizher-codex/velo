using System.Collections.ObjectModel;

namespace VELO.Core.Downloads;

public class DownloadManager
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public ObservableCollection<DownloadItem> Downloads { get; } = [];

    public event EventHandler<DownloadItem>? DownloadStarted;

    public DownloadItem StartDownload(string url, string fileName, string filePath, long totalBytes)
    {
        var item = new DownloadItem
        {
            Url        = url,
            FileName   = fileName,
            FilePath   = filePath,
            TotalBytes = totalBytes,
            State      = DownloadState.InProgress
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
