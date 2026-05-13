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

    // Phase 4.0 (Council Mode) — four built-in containers, one per LLM panel.
    // IDs match VELO.Core.Containers.CouncilContainerPolicy.CouncilContainerIds
    // in panel-index order (0=top-left through 3=bottom-right). Colours pick
    // each provider's brand tone so the user can recognise which Council
    // panel is which at a glance.
    public static readonly Container CouncilClaude  = new() { Id = "council-claude",  Name = "Claude",  Color = "#D97757" }; // Anthropic warm
    public static readonly Container CouncilChatGpt = new() { Id = "council-chatgpt", Name = "ChatGPT", Color = "#10A37F" }; // OpenAI green
    public static readonly Container CouncilGrok    = new() { Id = "council-grok",    Name = "Grok",    Color = "#1DA1F2" }; // xAI / Twitter-ish blue
    public static readonly Container CouncilOllama  = new() { Id = "council-ollama",  Name = "Ollama",  Color = "#A78BFA" }; // VELO violet — local model

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
