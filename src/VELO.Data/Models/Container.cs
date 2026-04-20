using SQLite;

namespace VELO.Data.Models;

[Table("containers")]
public class Container
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [NotNull]
    public string Name { get; set; } = "";

    [NotNull]
    public string Color { get; set; } = "#808080";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Self-Destruct TTL — null means no expiry
    public DateTime? ExpiresAt { get; set; }

    // Banking-mode: enforces stricter isolation rules
    public bool IsBankingMode { get; set; }

    public bool IsTemporary => ExpiresAt.HasValue;
    public bool IsExpired   => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    // Built-in containers
    public static readonly Container Personal = new() { Id = "personal", Name = "Personal",  Color = "#00E5FF" };
    public static readonly Container Work     = new() { Id = "work",     Name = "Trabajo",    Color = "#7FFF5F" };
    public static readonly Container Banking  = new() { Id = "banking",  Name = "Banca",      Color = "#FF3D71", IsBankingMode = true };
    public static readonly Container Shopping = new() { Id = "shopping", Name = "Compras",    Color = "#FFB300" };
    public static readonly Container None     = new() { Id = "none",     Name = "Sin container", Color = "#808080" };

    /// <summary>Creates a temporary container that auto-destructs after the given duration.</summary>
    public static Container Temporary(string name, string color, TimeSpan ttl) => new()
    {
        Id        = Guid.NewGuid().ToString(),
        Name      = name,
        Color     = color,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.Add(ttl),
    };
}
