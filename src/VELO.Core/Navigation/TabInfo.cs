using System.ComponentModel;
using System.Runtime.CompilerServices;
using VELO.Core.Localization;

namespace VELO.Core.Navigation;

public class TabInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public string Id { get; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    private string _url = "velo://newtab";
    public string Url { get => _url; set => Set(ref _url, value); }

    private string _title = LocalizationService.Current.T("newtab.title");
    public string Title { get => _title; set => Set(ref _title, value); }

    private string _containerId = "none";
    public string ContainerId
    {
        get => _containerId;
        set
        {
            Set(ref _containerId, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContainerColor)));
        }
    }

    public string ContainerColor => _containerId switch
    {
        "personal" => "#00E5FF",
        "work"     => "#7FFF5F",
        "banking"  => "#FF3D71",
        "shopping" => "#FFB300",
        _          => "Transparent"
    };

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _canGoBack;
    public bool CanGoBack { get => _canGoBack; set => Set(ref _canGoBack, value); }

    private bool _canGoForward;
    public bool CanGoForward { get => _canGoForward; set => Set(ref _canGoForward, value); }

    public byte[]? FaviconData { get; set; }

    private string _workspaceId = "default";
    public string WorkspaceId { get => _workspaceId; set => Set(ref _workspaceId, value); }
}
