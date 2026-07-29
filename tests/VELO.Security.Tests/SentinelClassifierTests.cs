using VELO.Security.Sentinel;
using Xunit;

namespace VELO.Security.Tests;

/// <summary>
/// S-C — the parts of Sentinel that must hold with or without a model on
/// disk: host normalisation, the two-level decision rule, model discovery,
/// manifest handling, and the fail-soft contract. End-to-end behaviour with
/// a real model lives in <see cref="SentinelModelIntegrationTests"/>.
/// </summary>
public class SentinelClassifierTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "velo-sentinel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // ── Host normalisation — the model's input contract ───────────────────

    [Theory]
    [InlineData("github.com",                        "github.com")]
    [InlineData("GitHub.COM",                        "github.com")]
    [InlineData("https://github.com/foo/bar",        "github.com")]
    [InlineData("http://github.com:8080/",           "github.com")]
    [InlineData("github.com:443",                    "github.com")]
    [InlineData("https://user:pw@github.com/x",      "github.com")]
    [InlineData("github.com.",                       "github.com")]   // trailing root dot
    [InlineData("  github.com  ",                    "github.com")]
    [InlineData("",                                  "")]
    [InlineData("   ",                               "")]
    public void NormalizeHost_strips_everything_that_is_not_the_host(string input, string expected)
        => Assert.Equal(expected, SentinelClassifier.NormalizeHost(input));

    [Fact]
    public void NormalizeHost_refuses_ipv6_literals()
    {
        // An address is not a name — there is nothing in it for the model to
        // read, and the bracket syntax would confuse the tokenizer. RequestGuard
        // already has dedicated rules for literal targets.
        Assert.Equal("", SentinelClassifier.NormalizeHost("[2001:db8::1]"));
        Assert.Equal("", SentinelClassifier.NormalizeHost("https://[2001:db8::1]:8443/x"));
    }

    // ── The two-level decision rule (must match evaluate.py's decide()) ───

    [Fact]
    public void Benign_argmax_is_always_Allow()
    {
        var result = SentinelClassifier.Decide([0.97f, 0.01f, 0.01f, 0.01f], 0.85);
        Assert.Equal(SentinelLabel.Benign, result.Label);
        Assert.Equal(SentinelAction.Allow, result.Action);
    }

    [Fact]
    public void Non_benign_above_the_threshold_is_Block()
    {
        var result = SentinelClassifier.Decide([0.05f, 0.90f, 0.03f, 0.02f], 0.85);
        Assert.Equal(SentinelLabel.Phishing, result.Label);
        Assert.Equal(SentinelAction.Block,   result.Action);
        Assert.Contains("phishing", result.Reason);
        Assert.Contains("0.9",      result.Reason);   // the verdict says why (lesson #29)
    }

    [Fact]
    public void Phishing_below_the_threshold_is_Flag_not_Block()
    {
        // The S-B semantics: a phishing belief that doesn't clear the bar is a
        // SIGNAL for PhishingShield, never a block on its own.
        var result = SentinelClassifier.Decide([0.30f, 0.60f, 0.05f, 0.05f], 0.85);
        Assert.Equal(SentinelLabel.Phishing, result.Label);
        Assert.Equal(SentinelAction.Flag,    result.Action);
    }

    [Fact]
    public void Tracker_or_ad_below_the_threshold_collapses_to_Allow()
    {
        // Unlike phishing, a half-confident tracker guess is worth nothing: the
        // blocklists already do that job exactly, and this is the cheapest place
        // to spend a user's trust.
        var tracker = SentinelClassifier.Decide([0.35f, 0.05f, 0.55f, 0.05f], 0.85);
        Assert.Equal(SentinelAction.Allow,  tracker.Action);
        Assert.Equal(SentinelLabel.Benign,  tracker.Label);

        var ad = SentinelClassifier.Decide([0.30f, 0.05f, 0.05f, 0.60f], 0.85);
        Assert.Equal(SentinelAction.Allow, ad.Action);
    }

    [Fact]
    public void The_threshold_is_the_manifest_value_not_a_constant()
    {
        // Same probabilities, two operating points: a retrained model that
        // publishes a different conf_threshold_block must actually change the
        // verdict, or the manifest is decoration.
        float[] probs = [0.10f, 0.75f, 0.10f, 0.05f];

        Assert.Equal(SentinelAction.Flag,  SentinelClassifier.Decide(probs, 0.85).Action);
        Assert.Equal(SentinelAction.Block, SentinelClassifier.Decide(probs, 0.70).Action);
    }

    [Fact]
    public void Exactly_at_the_threshold_blocks()
    {
        // evaluate.py uses `>=`; so must we, or the C# side is stricter than the
        // gate that measured the FPR.
        Assert.Equal(SentinelAction.Block,
            SentinelClassifier.Decide([0.15f, 0.85f, 0f, 0f], 0.85).Action);
    }

    [Fact]
    public void Empty_model_output_fails_soft()
        => Assert.Equal(SentinelAction.Allow, SentinelClassifier.Decide([], 0.85).Action);

    [Fact]
    public void Softmax_normalises_and_preserves_ordering()
    {
        var probs = SentinelClassifier.Softmax([2.0f, 1.0f, 0.1f, -1.0f]);

        Assert.Equal(1.0, probs.Sum(), precision: 5);
        Assert.True(probs[0] > probs[1] && probs[1] > probs[2] && probs[2] > probs[3]);
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }

    [Fact]
    public void Softmax_survives_large_logits()
    {
        // Max-subtraction, same as the Python side — without it int8 logits at
        // the tails overflow to NaN and every verdict becomes garbage.
        var probs = SentinelClassifier.Softmax([1000f, 999f, -1000f, 0f]);
        Assert.All(probs, p => Assert.False(float.IsNaN(p)));
        Assert.Equal(1.0, probs.Sum(), precision: 5);
    }

    // ── Model discovery ──────────────────────────────────────────────────

    [Fact]
    public void LocateModelDirectory_returns_null_when_nothing_is_installed()
    {
        Assert.Null(SentinelClassifier.LocateModelDirectory(Path.Combine(_tempRoot, "nope")));

        Directory.CreateDirectory(_tempRoot);
        Assert.Null(SentinelClassifier.LocateModelDirectory(_tempRoot));
    }

    [Fact]
    public void LocateModelDirectory_ignores_incomplete_directories()
    {
        // A half-finished download (S-D will write into these) must never be
        // picked up as a usable model.
        var partial = Path.Combine(_tempRoot, "v1");
        Directory.CreateDirectory(partial);
        File.WriteAllText(Path.Combine(partial, SentinelClassifier.ModelFileName), "x");
        File.WriteAllText(Path.Combine(partial, SentinelClassifier.ManifestFileName), "{}");
        // tokenizer.json missing

        Assert.Null(SentinelClassifier.LocateModelDirectory(_tempRoot));
    }

    [Fact]
    public void LocateModelDirectory_picks_the_highest_version()
    {
        FakeModelDir("v1");
        FakeModelDir("v10");    // numeric, not lexicographic — v10 > v9
        FakeModelDir("v9");

        var chosen = SentinelClassifier.LocateModelDirectory(_tempRoot);
        Assert.Equal("v10", Path.GetFileName(chosen));
    }

    [Fact]
    public void LocateModelDirectory_accepts_assets_dropped_in_the_root()
    {
        // How you install a release by hand before S-D lands the downloader.
        Directory.CreateDirectory(_tempRoot);
        WriteModelAssets(_tempRoot);

        Assert.Equal(_tempRoot.TrimEnd(Path.DirectorySeparatorChar),
                     SentinelClassifier.LocateModelDirectory(_tempRoot)?.TrimEnd(Path.DirectorySeparatorChar));
    }

    // ── Manifest ─────────────────────────────────────────────────────────

    [Fact]
    public void Manifest_reads_the_published_model_v1_shape()
    {
        var manifest = SentinelManifest.FromJson(ManifestJson(version: 1, schema: 1, threshold: 0.85));

        Assert.Equal("velo-sentinel", manifest.Model);
        Assert.Equal(1,    manifest.Version);
        Assert.Equal(1,    manifest.Schema);
        Assert.Equal(32,   manifest.MaxLen);
        Assert.Equal(0.85, manifest.BlockThreshold);
        Assert.Equal(new[] { "benign", "phishing", "tracker", "ad" }, manifest.Labels);
        Assert.True(manifest.IsSchemaSupported);
    }

    [Fact]
    public void Manifest_rejects_a_schema_this_build_cannot_run()
    {
        // MinModelSchema/MaxModelSchema exist so a future input contract (URLs
        // instead of hosts, a different tokenizer) can't be fed to today's
        // pipeline just because the file names line up.
        Assert.False(SentinelManifest.FromJson(ManifestJson(2, 99, 0.85)).IsSchemaSupported);
        Assert.False(SentinelManifest.FromJson(ManifestJson(2,  0, 0.85)).IsSchemaSupported);
    }

    [Fact]
    public void Unsupported_schema_disables_the_classifier_instead_of_guessing()
    {
        FakeModelDir("v2", schema: 99);

        using var sentinel = new SentinelClassifier(modelRoot: _tempRoot);
        Assert.False(sentinel.EnsureLoaded());
        Assert.False(sentinel.IsLoaded);
        Assert.Contains("schema", sentinel.Status);
        Assert.Equal(SentinelAction.Allow, sentinel.Classify("anything.com").Action);
    }

    // ── Fail-soft contract ───────────────────────────────────────────────

    [Fact]
    public void No_model_installed_allows_everything()
    {
        using var sentinel = new SentinelClassifier(modelRoot: Path.Combine(_tempRoot, "missing"));

        Assert.False(sentinel.EnsureLoaded());
        Assert.False(sentinel.IsLoaded);
        Assert.Equal("model not installed", sentinel.Status);

        var result = sentinel.Classify("paypal-secure-verification.top");
        Assert.Equal(SentinelAction.Allow, result.Action);
        Assert.Equal(SentinelLabel.Benign, result.Label);
    }

    [Fact]
    public void No_model_installed_makes_Prefetch_a_no_op()
    {
        using var sentinel = new SentinelClassifier(modelRoot: Path.Combine(_tempRoot, "missing"));
        sentinel.EnsureLoaded();

        sentinel.Prefetch("example.com");

        Assert.Equal(0, sentinel.CacheCount);
        Assert.Null(sentinel.TryGetCachedVerdict("example.com"));
    }

    [Fact]
    public void A_corrupt_model_directory_fails_soft()
    {
        FakeModelDir("v1");   // the .onnx is a text placeholder — the session build must throw

        using var sentinel = new SentinelClassifier(modelRoot: _tempRoot);
        Assert.False(sentinel.EnsureLoaded());
        Assert.StartsWith("load failed", sentinel.Status);
        Assert.Equal(SentinelAction.Allow, sentinel.Classify("example.com").Action);
    }

    [Fact]
    public void Empty_host_never_reaches_the_model()
    {
        using var sentinel = new SentinelClassifier(modelRoot: Path.Combine(_tempRoot, "missing"));

        Assert.Equal(SentinelAction.Allow, sentinel.Classify("").Action);
        Assert.Equal(SentinelAction.Allow, sentinel.Classify(null).Action);
        Assert.Null(sentinel.TryGetCachedVerdict(""));
    }

    [Fact]
    public void DescribeInstalledModel_reports_without_building_a_session()
    {
        var (missing, missingDir) = SentinelClassifier.DescribeInstalledModel(Path.Combine(_tempRoot, "missing"));
        Assert.Equal("not installed", missing);
        Assert.Null(missingDir);

        FakeModelDir("v1");
        var (status, dir) = SentinelClassifier.DescribeInstalledModel(_tempRoot);
        Assert.Contains("velo-sentinel v1", status);
        Assert.Contains("0.85", status);
        Assert.NotNull(dir);
    }

    // ── Cache ────────────────────────────────────────────────────────────

    [Fact]
    public void Cached_verdicts_expire_with_the_TTL()
    {
        using var sentinel = new SentinelClassifier(modelRoot: Path.Combine(_tempRoot, "missing"))
        {
            CacheTtl = TimeSpan.FromMilliseconds(-1),   // already expired on read
        };
        sentinel.SeedVerdict("tracker.example", new SentinelResult(
            SentinelLabel.Tracker, 0.99, SentinelAction.Block, "seeded"));

        Assert.Null(sentinel.TryGetCachedVerdict("tracker.example"));
    }

    [Fact]
    public void Cached_verdicts_are_keyed_by_normalised_host()
    {
        using var sentinel = new SentinelClassifier(modelRoot: Path.Combine(_tempRoot, "missing"));
        sentinel.SeedVerdict("tracker.example", new SentinelResult(
            SentinelLabel.Tracker, 0.99, SentinelAction.Block, "seeded"));

        var hit = sentinel.TryGetCachedVerdict("https://Tracker.Example:443/beacon?x=1");
        Assert.NotNull(hit);
        Assert.True(hit!.FromCache);
        Assert.Equal(SentinelAction.Block, hit.Action);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void FakeModelDir(string name, int schema = 1)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        WriteModelAssets(dir, schema);
    }

    private static void WriteModelAssets(string dir, int schema = 1)
    {
        // Not a real ONNX graph — enough for discovery/manifest tests, and
        // deliberately unloadable so the fail-soft path is exercised too.
        File.WriteAllText(Path.Combine(dir, SentinelClassifier.ModelFileName), "not-an-onnx-graph");
        File.WriteAllText(Path.Combine(dir, SentinelClassifier.TokenizerFileName),
            """{"model":{"type":"WordPiece","vocab":{"[PAD]":0,"[UNK]":100,"[CLS]":101,"[SEP]":102}}}""");
        File.WriteAllText(Path.Combine(dir, SentinelClassifier.ManifestFileName),
            ManifestJson(version: 1, schema: schema, threshold: 0.85));
    }

    private static string ManifestJson(int version, int schema, double threshold) =>
        $$"""
        {
          "model": "velo-sentinel",
          "version": {{version}},
          "schema": {{schema}},
          "max_len": 32,
          "labels": ["benign", "phishing", "tracker", "ad"],
          "conf_threshold_block": {{threshold}}
        }
        """;
}
