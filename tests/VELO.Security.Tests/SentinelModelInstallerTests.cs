using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using VELO.Security.Sentinel;
using Xunit;

namespace VELO.Security.Tests;

/// <summary>
/// S-D — the download channel. What these pin is the refusal behaviour, not
/// the happy path: every failure mode has to end with the previously
/// installed model untouched and nothing half-written where
/// <see cref="SentinelClassifier.LocateModelDirectory"/> could find it. A
/// classifier that loads a corrupted or partially-downloaded model is worse
/// than one that never loads at all.
/// </summary>
public class SentinelModelInstallerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "velo-sentinel-installer", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed class StubHandler(Dictionary<string, byte[]> bodies) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!bodies.TryGetValue(request.RequestUri!.AbsoluteUri, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private const string ModelUrl     = "https://example.test/velo-sentinel.onnx";
    private const string TokenizerUrl = "https://example.test/tokenizer.json";
    private const string ManifestUrl  = "https://example.test/manifest.json";

    private static readonly byte[] ModelBytes     = Encoding.UTF8.GetBytes("pretend-onnx-graph");
    private static readonly byte[] TokenizerBytes = Encoding.UTF8.GetBytes("""{"model":{"type":"WordPiece","vocab":{}}}""");

    private static string Manifest(
        int version = 2, int schema = 1, string? modelHash = null, string? tokenizerHash = null) =>
        $$"""
        {
          "model": "velo-sentinel",
          "version": {{version}},
          "schema": {{schema}},
          "max_len": 32,
          "labels": ["benign", "phishing", "tracker", "ad"],
          "conf_threshold_block": 0.85,
          "files": {
            "velo-sentinel.onnx": { "sha256": "{{modelHash ?? Sha256Hex(ModelBytes)}}", "bytes": {{ModelBytes.Length}} },
            "tokenizer.json":     { "sha256": "{{tokenizerHash ?? Sha256Hex(TokenizerBytes)}}", "bytes": {{TokenizerBytes.Length}} }
          }
        }
        """;

    private SentinelModelInstaller Build(string manifestJson)
    {
        var handler = new StubHandler(new Dictionary<string, byte[]>
        {
            [ManifestUrl]  = Encoding.UTF8.GetBytes(manifestJson),
            [ModelUrl]     = ModelBytes,
            [TokenizerUrl] = TokenizerBytes,
        });
        return new SentinelModelInstaller(new HttpClient(handler), logger: null, modelRoot: _root);
    }

    private static SentinelModelInstaller.Available Release(int version = 2) =>
        new(version, $"model-v{version}", ManifestUrl, ModelUrl, TokenizerUrl);

    private string VersionDir(int version) => Path.Combine(_root, $"v{version}");

    private bool AnyStagingLeft()
        => Directory.Exists(_root)
        && Directory.EnumerateDirectories(_root, ".staging-*").Any();

    // ── Happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task Install_writes_a_complete_verified_version_directory()
    {
        var result = await Build(Manifest()).InstallAsync(Release());

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.Version);

        var dir = VersionDir(2);
        Assert.True(File.Exists(Path.Combine(dir, SentinelClassifier.ModelFileName)));
        Assert.True(File.Exists(Path.Combine(dir, SentinelClassifier.TokenizerFileName)));
        Assert.True(File.Exists(Path.Combine(dir, SentinelClassifier.ManifestFileName)));

        // Discovery must now find exactly what was installed.
        Assert.Equal(dir, SentinelClassifier.LocateModelDirectory(_root));
        Assert.False(AnyStagingLeft());
    }

    [Fact]
    public async Task Install_reports_progress_through_every_phase()
    {
        var phases = new List<SentinelModelInstaller.Phase>();
        var progress = new Progress<SentinelModelInstaller.Progress>(p => phases.Add(p.Phase));

        // Progress<T> posts asynchronously; collect synchronously instead so the
        // assertion doesn't race the callbacks.
        var sync = new SyncProgress();
        await Build(Manifest()).InstallAsync(Release(), sync);
        _ = progress;

        Assert.Contains(SentinelModelInstaller.Phase.Manifest,   sync.Phases);
        Assert.Contains(SentinelModelInstaller.Phase.Model,      sync.Phases);
        Assert.Contains(SentinelModelInstaller.Phase.Tokenizer,  sync.Phases);
        Assert.Contains(SentinelModelInstaller.Phase.Installing, sync.Phases);
    }

    private sealed class SyncProgress : IProgress<SentinelModelInstaller.Progress>
    {
        public List<SentinelModelInstaller.Phase> Phases { get; } = [];
        public void Report(SentinelModelInstaller.Progress value) => Phases.Add(value.Phase);
    }

    // ── Refusals ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_tampered_model_is_refused_and_nothing_is_installed()
    {
        // The whole point of publishing a hash. A model that fails
        // verification must not reach the model directory under any
        // circumstance — it is executed (as a graph) by the classifier.
        var badHash = new string('a', 64);
        var result = await Build(Manifest(modelHash: badHash)).InstallAsync(Release());

        Assert.False(result.Success);
        Assert.Contains("SHA256", result.Error);
        Assert.False(Directory.Exists(VersionDir(2)));
        Assert.Null(SentinelClassifier.LocateModelDirectory(_root));
        Assert.False(AnyStagingLeft());
    }

    [Fact]
    public async Task A_tampered_tokenizer_is_refused_too()
    {
        // A swapped tokenizer is as dangerous as a swapped model: same graph,
        // different token ids, confident nonsense out (lesson #33).
        var result = await Build(Manifest(tokenizerHash: new string('b', 64))).InstallAsync(Release());

        Assert.False(result.Success);
        Assert.False(Directory.Exists(VersionDir(2)));
    }

    [Fact]
    public async Task An_unsupported_schema_is_refused_before_downloading_67MB()
    {
        var result = await Build(Manifest(schema: 99)).InstallAsync(Release());

        Assert.False(result.Success);
        Assert.Contains("schema", result.Error);
        Assert.False(Directory.Exists(VersionDir(2)));
    }

    [Fact]
    public async Task A_tag_that_disagrees_with_the_manifest_is_refused()
    {
        // Tag says v2, manifest says v7 — the release was assembled wrong and
        // we cannot state what we would be installing.
        var result = await Build(Manifest(version: 7)).InstallAsync(Release(version: 2));

        Assert.False(result.Success);
        Assert.Contains("manifest", result.Error);
        Assert.False(Directory.Exists(VersionDir(2)));
    }

    [Fact]
    public async Task A_failed_install_leaves_the_existing_model_untouched()
    {
        // Install a good v2, then fail installing v3. v2 has to survive: a
        // failed upgrade must never cost the user the model they had.
        Assert.True((await Build(Manifest()).InstallAsync(Release())).Success);
        Assert.Equal(VersionDir(2), SentinelClassifier.LocateModelDirectory(_root));

        var bad = await Build(Manifest(version: 3, modelHash: new string('c', 64)))
            .InstallAsync(Release(version: 3));

        Assert.False(bad.Success);
        Assert.False(Directory.Exists(VersionDir(3)));
        Assert.Equal(VersionDir(2), SentinelClassifier.LocateModelDirectory(_root));
        Assert.False(AnyStagingLeft());
    }

    [Fact]
    public async Task A_missing_asset_url_fails_without_installing()
    {
        var handler = new StubHandler(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = Encoding.UTF8.GetBytes(Manifest()),
            // model + tokenizer deliberately absent → 404
        });
        var installer = new SentinelModelInstaller(new HttpClient(handler), logger: null, modelRoot: _root);

        var result = await installer.InstallAsync(Release());

        Assert.False(result.Success);
        Assert.False(Directory.Exists(VersionDir(2)));
        Assert.False(AnyStagingLeft());
    }

    // ── InstalledVersion / hash parsing ──────────────────────────────────

    [Fact]
    public async Task InstalledVersion_reflects_what_is_on_disk()
    {
        var installer = Build(Manifest());
        Assert.Equal(0, installer.InstalledVersion());

        await installer.InstallAsync(Release());
        Assert.Equal(2, installer.InstalledVersion());
    }

    [Fact]
    public void ExpectedHash_reads_the_manifest_files_section()
    {
        Assert.Equal(Sha256Hex(ModelBytes),
            SentinelModelInstaller.ExpectedHash(Manifest(), SentinelClassifier.ModelFileName));
        Assert.Null(SentinelModelInstaller.ExpectedHash(Manifest(), "not-in-the-manifest.bin"));
        Assert.Null(SentinelModelInstaller.ExpectedHash("{ not json", SentinelClassifier.ModelFileName));
    }

    // ── Reload ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Reload_picks_up_a_model_installed_while_running()
    {
        // Before S-D the load was one-shot: installing a model meant
        // restarting VELO. The classifier answers Allow before and reports the
        // new model after, without a restart.
        using var sentinel = new SentinelClassifier(modelRoot: _root);
        Assert.False(sentinel.EnsureLoaded());
        Assert.Equal(SentinelAction.Allow, sentinel.Classify("example.com").Action);

        await Build(Manifest()).InstallAsync(Release());

        // The stub model is not a real ONNX graph, so loading still fails —
        // but it must have RE-TRIED and reached the new directory rather than
        // remembering the earlier "nothing installed" answer.
        sentinel.Reload();
        Assert.NotEqual("model not installed", sentinel.Status);
        Assert.Equal(SentinelAction.Allow, sentinel.Classify("example.com").Action);
    }

    [Fact]
    public async Task Reload_clears_verdicts_from_the_previous_model()
    {
        // A new model's opinions are its own; keeping the old cache would make
        // the first minutes after an upgrade a silent mix of two models.
        using var sentinel = new SentinelClassifier(modelRoot: _root);
        sentinel.SeedVerdict("tracker.example", new SentinelResult(
            SentinelLabel.Tracker, 0.99, SentinelAction.Block, "from the old model"));
        Assert.NotNull(sentinel.TryGetCachedVerdict("tracker.example"));

        await Build(Manifest()).InstallAsync(Release());
        sentinel.Reload();

        Assert.Null(sentinel.TryGetCachedVerdict("tracker.example"));
    }
}
