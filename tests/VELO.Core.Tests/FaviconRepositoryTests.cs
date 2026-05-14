using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data;
using VELO.Data.Repositories;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// v2.4.43 — coverage for the favicon SQLite cache. Uses a real VeloDatabase
/// on a per-test temp folder so the schema is exercised end-to-end. Tests
/// invariants the tab sidebar depends on: TTL eviction, negative-cache marker,
/// host normalisation, and overwrite-on-resave semantics.
/// </summary>
public class FaviconRepositoryTests
{
    private static async Task<(FaviconRepository Repo, VeloDatabase Db)> BuildAsync()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        await db.InitializeAsync();
        return (new FaviconRepository(db), db);
    }

    // ── Host normalisation ──────────────────────────────────────────────

    [Theory]
    [InlineData("bambu.com",      "bambu.com")]
    [InlineData("www.bambu.com",  "bambu.com")]
    [InlineData("WWW.BAMBU.COM",  "bambu.com")]
    [InlineData("  WWW.bambu.COM ", "bambu.com")]
    [InlineData("github.com",     "github.com")]
    [InlineData("",               "")]
    public void Normalise_lowercasesAndStripsWww(string input, string expected)
    {
        Assert.Equal(expected, FaviconRepository.Normalise(input));
    }

    [Fact]
    public void Normalise_doesNotStripNonLeadingWww()
    {
        // "wwwexample.com" should not become "example.com" — only a literal
        // "www." prefix is dropped. Hosts like "wwwsmth.io" must survive.
        Assert.Equal("wwwexample.com", FaviconRepository.Normalise("wwwexample.com"));
    }

    // ── Save + GetFresh ─────────────────────────────────────────────────

    [Fact]
    public async Task GetFreshAsync_returnsBytesAfterSave()
    {
        var (repo, _) = await BuildAsync();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic

        await repo.SaveAsync("bambu.com", bytes);
        var fetched = await repo.GetFreshAsync("bambu.com");

        Assert.NotNull(fetched);
        Assert.Equal(bytes, fetched);
    }

    [Fact]
    public async Task GetFreshAsync_normalisesHostForLookup()
    {
        var (repo, _) = await BuildAsync();
        var bytes = new byte[] { 1, 2, 3 };

        await repo.SaveAsync("WWW.Bambu.COM", bytes);
        var byHost      = await repo.GetFreshAsync("bambu.com");
        var byUppercase = await repo.GetFreshAsync("WWW.BAMBU.COM");

        Assert.Equal(bytes, byHost);
        Assert.Equal(bytes, byUppercase);
    }

    [Fact]
    public async Task GetFreshAsync_returnsNullForUnknownHost()
    {
        var (repo, _) = await BuildAsync();
        var fetched = await repo.GetFreshAsync("never-cached.example");
        Assert.Null(fetched);
    }

    [Fact]
    public async Task GetFreshAsync_returnsNullForNullOrEmptyHost()
    {
        var (repo, _) = await BuildAsync();
        Assert.Null(await repo.GetFreshAsync(""));
        Assert.Null(await repo.GetFreshAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_overwritesOnSameHost()
    {
        var (repo, _) = await BuildAsync();
        var first  = new byte[] { 1, 1, 1 };
        var second = new byte[] { 2, 2, 2 };

        await repo.SaveAsync("bambu.com", first);
        await repo.SaveAsync("bambu.com", second);
        var fetched = await repo.GetFreshAsync("bambu.com");

        Assert.Equal(second, fetched);
    }

    // ── Negative cache ──────────────────────────────────────────────────

    [Fact]
    public async Task GetFreshAsync_returnsNullForNegativeCacheEntry()
    {
        // Empty array means "this host has no favicon, don't re-request".
        // GetFreshAsync should still return null so the converter shows 🌐
        // fallback — but the row itself is in cache so the same host doesn't
        // re-query until the row expires.
        var (repo, _) = await BuildAsync();
        await repo.SaveAsync("no-icon.example", Array.Empty<byte>());

        var fetched = await repo.GetFreshAsync("no-icon.example");
        Assert.Null(fetched);
    }

    // ── TTL eviction ────────────────────────────────────────────────────

    [Fact]
    public async Task GetFreshAsync_treatsRowOlderThanTtlAsExpired()
    {
        var (repo, _) = await BuildAsync();
        await repo.SaveAsync("bambu.com", new byte[] { 1 });

        // Asking with a TTL of zero forces every existing row to be considered
        // expired. The row is still there — eviction is a separate call —
        // GetFreshAsync just refuses to return stale bytes.
        var fetched = await repo.GetFreshAsync("bambu.com", TimeSpan.Zero);

        Assert.Null(fetched);
    }

    [Fact]
    public async Task PurgeExpiredAsync_removesStaleRows()
    {
        var (repo, db) = await BuildAsync();
        await repo.SaveAsync("a.example", new byte[] { 1 });
        await repo.SaveAsync("b.example", new byte[] { 2 });

        // Purge anything "older than 0 seconds" — both rows just-saved are
        // technically newer, so we need a slight wait to make them eligible.
        await Task.Delay(15);
        var removed = await repo.PurgeExpiredAsync(TimeSpan.FromMilliseconds(10));

        Assert.Equal(2, removed);
        Assert.Null(await repo.GetFreshAsync("a.example"));
        Assert.Null(await repo.GetFreshAsync("b.example"));
    }

    [Fact]
    public async Task PurgeExpiredAsync_keepsFreshRows()
    {
        var (repo, _) = await BuildAsync();
        await repo.SaveAsync("fresh.example", new byte[] { 9 });

        var removed = await repo.PurgeExpiredAsync(TimeSpan.FromDays(30));

        Assert.Equal(0, removed);
        Assert.NotNull(await repo.GetFreshAsync("fresh.example"));
    }

    // ── Explicit delete ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_removesEntry()
    {
        var (repo, _) = await BuildAsync();
        await repo.SaveAsync("bambu.com", new byte[] { 1, 2, 3 });

        await repo.DeleteAsync("bambu.com");

        Assert.Null(await repo.GetFreshAsync("bambu.com"));
    }

    [Fact]
    public async Task DeleteAsync_normalisesHost()
    {
        var (repo, _) = await BuildAsync();
        await repo.SaveAsync("bambu.com", new byte[] { 1 });

        // Saved as "bambu.com", deleting via "WWW.BAMBU.COM" should still work
        // because Normalise() canonicalises both into the same row key.
        await repo.DeleteAsync("WWW.BAMBU.COM");

        Assert.Null(await repo.GetFreshAsync("bambu.com"));
    }
}
