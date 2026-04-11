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

    // Built-in containers
    public static readonly Container Personal = new() { Id = "personal", Name = "Personal",  Color = "#00E5FF" };
    public static readonly Container Work     = new() { Id = "work",     Name = "Trabajo",    Color = "#7FFF5F" };
    public static readonly Container Banking  = new() { Id = "banking",  Name = "Banca",      Color = "#FF3D71" };
    public static readonly Container Shopping = new() { Id = "shopping", Name = "Compras",    Color = "#FFB300" };
    public static readonly Container None     = new() { Id = "none",     Name = "Sin container", Color = "#808080" };
}
