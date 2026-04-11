using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VELO.Core.Downloads;

public class DownloadItem : INotifyPropertyChanged
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long TotalBytes { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;

    private long _receivedBytes;
    public long ReceivedBytes
    {
        get => _receivedBytes;
        set { _receivedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(ReceivedText)); }
    }

    private DownloadState _state;
    public DownloadState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsInProgress)); }
    }

    public double Progress => TotalBytes > 0 ? (double)ReceivedBytes / TotalBytes * 100 : 0;
    public bool IsInProgress => State == DownloadState.InProgress;

    public string ReceivedText
    {
        get
        {
            static string Fmt(long b) => b switch
            {
                >= 1_073_741_824 => $"{b / 1_073_741_824.0:F1} GB",
                >= 1_048_576     => $"{b / 1_048_576.0:F1} MB",
                >= 1_024         => $"{b / 1_024.0:F0} KB",
                _                => $"{b} B"
            };
            if (TotalBytes > 0)
                return $"{Fmt(ReceivedBytes)} / {Fmt(TotalBytes)}";
            return ReceivedBytes > 0 ? Fmt(ReceivedBytes) : "";
        }
    }

    public string StatusText => State switch
    {
        DownloadState.Completed   => "Completado",
        DownloadState.Cancelled   => "Cancelado",
        DownloadState.Interrupted => "Interrumpido",
        _                         => ReceivedText
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum DownloadState { InProgress, Completed, Cancelled, Interrupted }
