using SQLite;

namespace VELO.Data.Models;

[Table("passwords")]
public class PasswordEntry
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [NotNull]
    public string SiteName { get; set; } = "";

    [NotNull]
    public string Url { get; set; } = "";

    [NotNull]
    public string Username { get; set; } = "";

    [NotNull]
    public string Password { get; set; } = ""; // stored encrypted

    public string? Notes { get; set; } // stored encrypted
    public string? ContainerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
