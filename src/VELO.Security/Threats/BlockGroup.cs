using System.Collections.ObjectModel;
using System.ComponentModel;

namespace VELO.Security.Threats;

/// <summary>
/// Phase 3 / Sprint 1 — Collapsible group of blocks for one host. Sort order
/// per spec (§ 2.5): Malwaredex hits first, then by Count descending, then
/// by latest BlockedAtUtc.
/// </summary>
public class BlockGroup : INotifyPropertyChanged
{
    public string Host                    { get; init; } = "";
    public ObservableCollection<BlockEntry> Entries { get; } = [];

    /// <summary>Dominant kind in the group — used for the icon at group level.</summary>
    public BlockKind TopKind => Entries.Count == 0
        ? BlockKind.Other
        : Entries.GroupBy(e => e.Kind).OrderByDescending(g => g.Count()).First().Key;

    public bool IsMalwaredexHit => Entries.Any(e => e.IsMalwaredexHit);

    public int Count => Entries.Count;
    public DateTime LatestUtc => Entries.Count == 0 ? DateTime.MinValue : Entries.Max(e => e.BlockedAtUtc);

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    /// <summary>Adds an entry and notifies aggregate property changes.</summary>
    public void Add(BlockEntry entry)
    {
        Entries.Add(entry);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopKind)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMalwaredexHit)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatestUtc)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
