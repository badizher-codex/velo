using SQLite;

namespace VELO.Data.Models;

[Table("bookmarks")]
public class Bookmark
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [NotNull]
    public string Url { get; set; } = "";

    [NotNull]
    public string Title { get; set; } = "";

    public string Folder { get; set; } = "root";
    public string? ContainerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public byte[]? FaviconBlob { get; set; }

    /// <summary>v2.4.18 — comma-separated, lowercase tags produced by BookmarkAIService.</summary>
    public string Tags { get; set; } = "";
}
