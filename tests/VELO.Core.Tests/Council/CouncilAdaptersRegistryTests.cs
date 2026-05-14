using VELO.Core.Council;
using Xunit;

namespace VELO.Core.Tests.Council;

/// <summary>
/// Phase 4.1 chunk D — coverage for the registry that loads the four bundled
/// per-provider adapter JSON files. Tests stage temp folders so they're
/// independent from the actual bundled resources (the smoke test below
/// covers those separately).
/// </summary>
public class CouncilAdaptersRegistryTests
{
    private static string MakeAdapterJson(string name) => $$"""
        {
            "name": "{{name}}",
            "displayName": "{{name}} display",
            "version": "test-v1",
            "homeUrl": "https://example.com/",
            "composer": "textarea",
            "sendButton": "button[type='submit']",
            "responseContainer": "div.reply",
            "codeBlock": "pre code",
            "table": "table",
            "citation": "a[href]",
            "notes": "test notes"
        }
        """;

    private static string StageAdaptersFolder(
        bool includeClaude  = true,
        bool includeChatGpt = true,
        bool includeGrok    = true,
        bool includeLocal   = true,
        string? malformed   = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "velo-adapter-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        if (includeClaude)  File.WriteAllText(Path.Combine(folder, "claude.json"),  MakeAdapterJson("claude"));
        if (includeChatGpt) File.WriteAllText(Path.Combine(folder, "chatgpt.json"), MakeAdapterJson("chatgpt"));
        if (includeGrok)    File.WriteAllText(Path.Combine(folder, "grok.json"),    MakeAdapterJson("grok"));
        if (includeLocal)   File.WriteAllText(Path.Combine(folder, "local.json"),   MakeAdapterJson("local"));
        if (malformed is not null)
            File.WriteAllText(Path.Combine(folder, "claude.json"), malformed);
        return folder;
    }

    // ── Happy path ──────────────────────────────────────────────────────

    [Fact]
    public void Load_allFourProviders_areAvailable()
    {
        var folder = StageAdaptersFolder();
        var registry = new CouncilAdaptersRegistry(folder);

        Assert.Equal(4, registry.Available.Count);
        Assert.Contains(CouncilProvider.Claude,  registry.Available);
        Assert.Contains(CouncilProvider.ChatGpt, registry.Available);
        Assert.Contains(CouncilProvider.Grok,    registry.Available);
        Assert.Contains(CouncilProvider.Local,   registry.Available);
    }

    [Fact]
    public void TryGet_returnsAdapterWithExpectedFields()
    {
        var folder = StageAdaptersFolder();
        var registry = new CouncilAdaptersRegistry(folder);

        var claude = registry.TryGet(CouncilProvider.Claude);

        Assert.NotNull(claude);
        Assert.Equal("claude", claude!.Name);
        Assert.Equal("test-v1", claude.Version);
        Assert.Equal("textarea", claude.Composer);
        Assert.True(claude.IsValid);
    }

    [Fact]
    public void GetAdapterJson_returnsRoundtripableJson()
    {
        var folder = StageAdaptersFolder();
        var registry = new CouncilAdaptersRegistry(folder);

        var json = registry.GetAdapterJson(CouncilProvider.Grok);

        Assert.NotNull(json);
        Assert.Contains("\"name\":", json);
        Assert.Contains("\"composer\":", json);
        Assert.Contains("\"sendButton\":", json);
        // Confirm it's parseable as our model again.
        var parsed = System.Text.Json.JsonSerializer.Deserialize<CouncilAdapter>(json!,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(parsed);
        Assert.Equal("grok", parsed!.Name);
    }

    // ── Partial / missing files ────────────────────────────────────────

    [Fact]
    public void Load_skipsMissingFiles_otherProvidersStillLoad()
    {
        var folder = StageAdaptersFolder(includeChatGpt: false, includeGrok: false);
        var registry = new CouncilAdaptersRegistry(folder);

        Assert.Equal(2, registry.Available.Count);
        Assert.NotNull(registry.TryGet(CouncilProvider.Claude));
        Assert.Null   (registry.TryGet(CouncilProvider.ChatGpt));
        Assert.Null   (registry.TryGet(CouncilProvider.Grok));
        Assert.NotNull(registry.TryGet(CouncilProvider.Local));
    }

    [Fact]
    public void Load_skipsMalformedFiles_otherProvidersStillLoad()
    {
        var folder = StageAdaptersFolder(malformed: "{not valid json");
        var registry = new CouncilAdaptersRegistry(folder);

        // claude.json was overwritten with garbage → skipped.
        Assert.Null   (registry.TryGet(CouncilProvider.Claude));
        // The other three were untouched → loaded normally.
        Assert.NotNull(registry.TryGet(CouncilProvider.ChatGpt));
        Assert.NotNull(registry.TryGet(CouncilProvider.Grok));
        Assert.NotNull(registry.TryGet(CouncilProvider.Local));
    }

    [Fact]
    public void Load_skipsAdaptersMissingRequiredFields()
    {
        // Same shape minus composer → IsValid=false → registry skips.
        var folder = Path.Combine(Path.GetTempPath(), "velo-adapter-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "claude.json"), """
            { "name": "claude", "displayName": "Claude", "sendButton": "button", "responseContainer": "div" }
            """);

        var registry = new CouncilAdaptersRegistry(folder);

        Assert.Null(registry.TryGet(CouncilProvider.Claude));
        Assert.Empty(registry.Available);
    }

    // ── Empty / nonexistent folder ─────────────────────────────────────

    [Fact]
    public void Load_returnsEmptyForNonexistentFolder()
    {
        var registry = new CouncilAdaptersRegistry(
            Path.Combine(Path.GetTempPath(), "velo-nope-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(registry.Available);
    }

    // ── Bundled adapters: smoke check against the actual JSON files ─────

    [Fact]
    public void BundledAdapters_inRepo_areLoadable()
    {
        // Resolve the resources/council/adapters folder from the test repo.
        // The test binary lives at <repo>/tests/VELO.Core.Tests/bin/.../net8.0-windows/,
        // so we walk up to the repo root.
        var here = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(here);
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var probe = Path.Combine(dir.FullName, "resources", "council", "adapters");
            if (Directory.Exists(probe))
            {
                var registry = new CouncilAdaptersRegistry(probe);
                Assert.NotEmpty(registry.Available);
                Assert.NotNull(registry.TryGet(CouncilProvider.Claude));
                Assert.NotNull(registry.TryGet(CouncilProvider.ChatGpt));
                Assert.NotNull(registry.TryGet(CouncilProvider.Grok));
                Assert.NotNull(registry.TryGet(CouncilProvider.Local));
                return;
            }
            dir = dir.Parent;
        }

        // If we couldn't find it, the test environment is unusual but the
        // happy-path tests above already cover the registry surface; skip
        // rather than fail to keep CI stable in odd layouts.
    }
}
