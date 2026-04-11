using Microsoft.Extensions.Logging;
using VELO.Core.Events;

namespace VELO.Security.Rules;

public class BlocklistManager(EventBus eventBus, ILogger<BlocklistManager> logger)
{
    private readonly EventBus _eventBus = eventBus;
    private readonly ILogger<BlocklistManager> _logger = logger;

    /// <summary>Fired after a successful update so callers can persist the date.</summary>
    public event Action<DateTime>? UpdateSucceeded;

    private HashSet<string> _blockedDomains = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly (string Url, string Format)[] Sources =
    [
        ("https://easylist.to/easylist/easylist.txt",          "abp"),
        ("https://easylist.to/easylist/easyprivacy.txt",        "abp"),
        ("https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt", "abp"),
        ("https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/badware.txt", "abp"),
        ("https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts", "hosts"),
    ];

    public int DomainCount => _blockedDomains.Count;

    public bool IsBlocked(string domain)
    {
        domain = domain.ToLowerInvariant();

        if (_blockedDomains.Contains(domain)) return true;

        // Check parent domains (sub.tracker.com → tracker.com)
        var parts = domain.Split('.');
        for (int i = 1; i < parts.Length - 1; i++)
        {
            var parent = string.Join('.', parts.Skip(i));
            if (_blockedDomains.Contains(parent)) return true;
        }

        return false;
    }

    public async Task LoadBundledAsync(string resourcesPath)
    {
        var bundledPath = Path.Combine(resourcesPath, "blocklists");
        if (!Directory.Exists(bundledPath)) return;

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(bundledPath, "*.txt"))
        {
            await ParseFileAsync(file, domains);
        }

        await _lock.WaitAsync();
        try { _blockedDomains = domains; }
        finally { _lock.Release(); }

        _logger.LogInformation("Bundled blocklists loaded: {Count} domains", domains.Count);
    }

    public async Task LoadCachedAsync()
    {
        var cacheDir = CacheDir();
        if (!Directory.Exists(cacheDir)) return;

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(cacheDir, "*.txt"))
            await ParseFileAsync(file, domains);

        if (domains.Count == 0) return;

        await _lock.WaitAsync();
        try { _blockedDomains = domains; }
        finally { _lock.Release(); }

        _logger.LogInformation("Cached blocklists loaded: {Count} domains", domains.Count);
    }

    public async Task UpdateAsync(DateTime? lastSuccessfulUpdate)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("User-Agent", "VELO-Browser/2.0");

        var cacheDir = CacheDir();
        Directory.CreateDirectory(cacheDir);

        int idx = 0;
        foreach (var (url, format) in Sources)
        {
            try
            {
                var content = await http.GetStringAsync(url);
                ParseContent(content, format, domains);

                // Save to cache so next startup loads instantly
                var fileName = $"list_{idx:D2}.txt";
                await File.WriteAllTextAsync(Path.Combine(cacheDir, fileName), content);

                _logger.LogDebug("Blocklist updated from {Url}: {Count} entries", url, domains.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download blocklist from {Url}", url);
                _eventBus.Publish(new BlocklistUpdateFailedEvent(lastSuccessfulUpdate));
                return;
            }
            idx++;
        }

        await _lock.WaitAsync();
        try { _blockedDomains = domains; }
        finally { _lock.Release(); }

        var now = DateTime.UtcNow;
        _logger.LogInformation("Blocklists updated: {Count} domains at {Time}", domains.Count, now);
        UpdateSucceeded?.Invoke(now);
    }

    private static string CacheDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VELO", "blocklists");

    private static async Task ParseFileAsync(string path, HashSet<string> output)
    {
        var content = await File.ReadAllTextAsync(path);
        var ext = Path.GetExtension(path).ToLower();
        ParseContent(content, ext == ".hosts" ? "hosts" : "abp", output);
    }

    private static void ParseContent(string content, string format, HashSet<string> output)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('!') || trimmed.StartsWith('#'))
                continue;

            if (format == "hosts")
            {
                // "0.0.0.0 tracker.example.com"
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && (parts[0] == "0.0.0.0" || parts[0] == "127.0.0.1"))
                    output.Add(parts[1].ToLowerInvariant());
            }
            else // abp format: "||tracker.example.com^"
            {
                if (trimmed.StartsWith("||") && trimmed.EndsWith("^"))
                {
                    var domain = trimmed[2..^1];
                    if (!domain.Contains('/') && !domain.Contains('*'))
                        output.Add(domain.ToLowerInvariant());
                }
            }
        }
    }
}
