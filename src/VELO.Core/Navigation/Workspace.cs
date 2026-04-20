using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VELO.Core.Navigation;

public class Workspace : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return;
        f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    private string _name = "Workspace";
    public string Name { get => _name; set => Set(ref _name, value); }

    private string _color = "#808080";
    public string Color { get => _color; set => Set(ref _color, value); }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    public static readonly Workspace Default = new()
    {
        Id    = "default",
        Name  = "Principal",
        Color = "#00E5FF",
    };
}
