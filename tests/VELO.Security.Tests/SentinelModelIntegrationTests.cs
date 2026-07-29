using System.Diagnostics;
using VELO.Security.Sentinel;
using Xunit;
using Xunit.Abstractions;

namespace VELO.Security.Tests;

/// <summary>
/// S-C — end-to-end checks against a REAL installed model. These only assert
/// when a model is present under <c>%LOCALAPPDATA%\VELO\models\sentinel\</c>;
/// on a machine (or CI runner) without one they verify the fail-soft path
/// instead and return, because the download channel is S-D and the model is
/// deliberately not in the installer.
///
/// To run them for real, drop the <c>model-v1</c> release assets
/// (velo-sentinel.onnx + tokenizer.json + manifest.json) into
/// <c>%LOCALAPPDATA%\VELO\models\sentinel\v1\</c>.
///
/// What they pin, when a model IS installed:
///   • the ONNX input/output contract (input_ids + attention_mask int64 →
///     logits) still matches what export_onnx.py produced;
///   • the never-block regression list still comes back Allow — the same list
///     evaluate.py gates on, so a C#-side drift in tokenizing or in the
///     decision rule shows up here and not in the field;
///   • latency stays under the 50 ms gate from PLAN §4.
/// </summary>
public class SentinelModelIntegrationTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>Hosts that must never reach a BLOCK verdict — a subset of
    /// <c>training/sentinel/regression_never_block.txt</c> plus the streaming
    /// hosts F-1 cost two months of debugging.</summary>
    private static readonly string[] NeverBlock =
    [
        "github.com", "www.google.com", "google.com", "mail.google.com",
        "primevideo.com", "www.primevideo.com", "netflix.com", "youtube.com",
        "bbc.co.uk", "chase.com", "paypal.com", "www.paypal.com",
        "afip.gob.ar", "cdn.jsdelivr.net", "microsoft.com", "login.microsoftonline.com",
        // Asset/media CDNs, added after the first shadow-mode session. Blocking
        // any of these is indistinguishable from "the site is broken".
        "rr7---sn-0opoxu-j8we.googlevideo.com", "i.ytimg.com", "yt3.ggpht.com",
        "assets.grok.com", "external-content.duckduckgo.com",
    ];

    /// <summary>
    /// Hosts the CURRENT model gets wrong, kept visible instead of quietly
    /// removed from the list above.
    ///
    /// <c>cdn.jsdelivr.net</c> came back "ad" at p=0.92 during S-C. The first
    /// real shadow-mode session (2026-07-29) showed that was the small version
    /// of the problem: YouTube's media and image CDNs come back tracker or
    /// phishing at p=0.90-0.99, which in enforce mode reads as "YouTube does
    /// not play". Two learned shortcuts are behind all of it — "CDN-shaped
    /// subdomain = tracker" and "machine-generated hostname = phishing" — and
    /// the benign side of the training set (Tranco root domains plus synthetic
    /// subdomains) gave the model no reason to know better.
    ///
    /// Contained today by Shadow mode, which is exactly why S-E exists. Fixed
    /// properly by model-v2: every host here is now in
    /// <c>training/sentinel/regression_never_block.txt</c>, so the Python gate
    /// refuses to publish a model that still misses them, and this set goes
    /// back to empty.
    ///
    /// <b>Sentinel must not be switched to Enforce while this set is
    /// non-empty.</b>
    /// </summary>
    private static readonly HashSet<string> KnownModelV1Misses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.jsdelivr.net",
        "rr7---sn-0opoxu-j8we.googlevideo.com",
        "i.ytimg.com",
        "yt3.ggpht.com",
        "assets.grok.com",
        "external-content.duckduckgo.com",
    };

    [Fact]
    public void Installed_model_loads_and_answers_within_the_latency_gate()
    {
        using var sentinel = new SentinelClassifier();

        var loadWatch = Stopwatch.StartNew();
        var loaded = sentinel.EnsureLoaded();
        loadWatch.Stop();

        if (!loaded)
        {
            // Fail-soft is the assertion when there is no model to load.
            _output.WriteLine($"no model under {sentinel.ModelRoot} — asserting fail-soft only ({sentinel.Status})");
            var result = sentinel.Classify("obviously-not-a-real-host.example");
            Assert.Equal(SentinelAction.Allow, result.Action);
            Assert.False(sentinel.IsLoaded);
            return;
        }

        _output.WriteLine($"loaded in {loadWatch.ElapsedMilliseconds} ms — {sentinel.Status} — {sentinel.ModelDirectory}");
        Assert.NotNull(sentinel.Manifest);
        Assert.True(sentinel.Manifest!.IsSchemaSupported);

        sentinel.Classify("example.com");   // warm-up: first Run pays JIT + arena setup

        var times = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            sentinel.ClearCache();
            var watch = Stopwatch.StartNew();
            sentinel.Classify($"host{i}.example.com");
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();

        var p50 = times[times.Count / 2];
        var p95 = times[(int)(times.Count * 0.95)];
        _output.WriteLine($"single-thread latency over {times.Count} hosts: p50 {p50:0.0} ms · p95 {p95:0.0} ms");

        // PLAN_VELO_IA_SEGURIDAD.md §4 quality gate.
        Assert.True(p95 < 50, $"p95 {p95:0.0} ms exceeds the 50 ms gate");
    }

    [Fact]
    public void Installed_model_never_blocks_the_regression_list()
    {
        using var sentinel = new SentinelClassifier();
        if (!sentinel.EnsureLoaded())
        {
            _output.WriteLine("no model installed — skipping the never-block regression");
            return;
        }

        var blocked = new List<string>();
        var knownMissesStillFailing = new List<string>();

        foreach (var host in NeverBlock)
        {
            var result = sentinel.Classify(host);
            _output.WriteLine($"{host,-32} {result.Label,-9} p={result.Confidence:0.0000} {result.Action}");

            if (result.Action != SentinelAction.Block) continue;

            if (KnownModelV1Misses.Contains(host))
                knownMissesStillFailing.Add(host);
            else
                blocked.Add($"{host} → {result.Label} p={result.Confidence:0.###}");
        }

        if (knownMissesStillFailing.Count > 0)
        {
            // Only the generic script CDNs are in TrustedHosts; the media CDNs
            // are held back by Shadow mode alone. Say which, so nobody reads
            // this line as "handled".
            _output.WriteLine(
                "known model-v1 misses (Shadow mode is what keeps these from breaking pages — " +
                "do NOT enable Enforce until model-v2 clears them): " +
                string.Join(", ", knownMissesStillFailing));
        }

        Assert.True(blocked.Count == 0,
            "Sentinel would BLOCK hosts on the never-block list:\n  " + string.Join("\n  ", blocked) +
            "\nIf this is a genuine new model regression, fix the model — do not add the host to " +
            "KnownModelV1Misses without the mitigation and a note.");
    }

    [Fact]
    public void Classification_is_cached_per_host()
    {
        using var sentinel = new SentinelClassifier();
        if (!sentinel.EnsureLoaded())
        {
            _output.WriteLine("no model installed — skipping the cache round-trip");
            return;
        }

        sentinel.ClearCache();
        Assert.Null(sentinel.TryGetCachedVerdict("github.com"));

        var first = sentinel.Classify("github.com");
        Assert.False(first.FromCache);

        // Scheme/port/path all normalise to the same cache key.
        var cached = sentinel.TryGetCachedVerdict("https://GitHub.com:443/foo/bar");
        Assert.NotNull(cached);
        Assert.True(cached!.FromCache);
        Assert.Equal(first.Label, cached.Label);
    }
}
