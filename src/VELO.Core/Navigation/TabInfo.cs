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
    public string Url
    {
        get => _url;
        set
        {
            Set(ref _url, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Initial)));
        }
    }

    private string _title = LocalizationService.Current.T("newtab.title");
    public string Title
    {
        get => _title;
        set
        {
            Set(ref _title, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Initial)));
        }
    }

    private string _containerId = "none";
    public string ContainerId
    {
        get => _containerId;
        set
        {
            Set(ref _containerId, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContainerColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtleAccentColor)));
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

    // Eight muted accent tints — used when no container is assigned.
    // Format: #AARRGGBB (10 % opacity = 0x1A).
    private static readonly string[] _accentPalette =
    [
        "#1A4A7FC1", // cornflower blue
        "#1A3DA87A", // emerald
        "#1A9B59E8", // violet
        "#1AF5A623", // amber
        "#1AE55353", // rose
        "#1A0EA5E8", // sky blue
        "#1AEC4899", // pink
        "#1A5DA35D", // sage green
    ];

    /// <summary>
    /// Returns a subtle ARGB tint for the tab row background.
    /// Container tabs reuse their container colour at 10 % opacity; all
    /// others cycle through a palette keyed by the tab's immutable Id.
    /// </summary>
    public string SubtleAccentColor
    {
        get
        {
            if (_containerId != "none")
                return $"#1A{ContainerColor.TrimStart('#')}";

            var idx = Math.Abs(Id.GetHashCode()) % _accentPalette.Length;
            return _accentPalette[idx];
        }
    }

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

    /// <summary>
    /// v2.0.5 — Single-character glyph used by the collapsed sidebar.
    /// Prefers the first letter of the title; falls back to the host's first
    /// letter, then to '•'. Always uppercase.
    /// </summary>
    public string Initial
    {
        get
        {
            var src = _title;
            if (string.IsNullOrWhiteSpace(src) || src == LocalizationService.Current.T("newtab.title"))
            {
                if (Uri.TryCreate(_url, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
                    src = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                        ? u.Host[4..] : u.Host;
            }
            if (string.IsNullOrWhiteSpace(src)) return "•";
            foreach (var ch in src)
            {
                if (char.IsLetterOrDigit(ch))
                    return char.ToUpperInvariant(ch).ToString();
            }
            return "•";
        }
    }
}
