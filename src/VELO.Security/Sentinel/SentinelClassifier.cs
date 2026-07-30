using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VELO.Core;

namespace VELO.Security.Sentinel;

/// <summary>
/// S-C — VELO Sentinel: the embedded security classifier
/// (<c>PLAN_VELO_IA_SEGURIDAD.md</c>). A DistilBERT-class encoder, fine-tuned
/// on public feeds and quantised to int8, that answers one question in-process
/// and offline: <i>what kind of host is this?</i>
///
/// Contract, fixed by S-A/S-B and by the manifest shipped with the model:
///
/// • <b>Input is a HOST</b>, lowercase, no scheme / path / port. Not a URL —
///   S-B round 3 showed a URL model generalises by path shape and flags
///   github.com as phishing at p=1.000 because every synthetic benign path is
///   distinguishable. VELO's verdict pipeline is host-keyed anyway.
/// • <b>Output is two-level</b> (see <see cref="SentinelAction"/>): BLOCK only
///   at p ≥ <c>conf_threshold_block</c> (0.85 in model-v1, read from the
///   manifest — never hard-coded, so a retrained model brings its own
///   operating point); argmax==phishing below that is a FLAG that feeds
///   PhishingShield and never blocks alone.
/// • <b>Behind the blocklists.</b> The exact lists are faster and have no
///   false-positive surface; Sentinel covers the tail they never saw.
///   In front of the optional HTTP path (DirectChatAdapter stays opt-in).
/// • <b>Fail-soft, total.</b> No model on disk (the download channel is S-D),
///   an unreadable manifest, an unsupported schema, a throwing session — all
///   collapse to Allow, logged exactly once. The browser must never depend on
///   the model being there.
/// • <b>Shadow by default</b> (<see cref="SentinelMode"/>). S-E ships one
///   release that only records verdicts so they can be diffed against real
///   field logs before anything is allowed to cancel a request (lesson #30).
///
/// Threading: one ONNX session, single-threaded (<c>IntraOpNumThreads = 1</c>,
/// sequential execution) because this runs inside a browser and must not fight
/// the render path for cores — S-A measured 9.8 ms p50 that way, well under
/// the 50 ms gate. Inference is serialised behind <see cref="_inferenceLock"/>;
/// sync guards call <see cref="Prefetch"/> and read the cache on the next
/// request, the same shape <c>SmartBlockClassifier</c> uses.
/// </summary>
public sealed class SentinelClassifier : IDisposable
{
    public const string ModelFileName     = "velo-sentinel.onnx";
    public const string TokenizerFileName = "tokenizer.json";
    public const string ManifestFileName  = "manifest.json";

    /// <summary>Verdicts stay cached this long. Hosts don't change what they are.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Shadow (record only) or Enforce (verdicts apply). Set by the host from settings.</summary>
    public SentinelMode Mode { get; set; } = SentinelMode.Shadow;

    private readonly ILogger<SentinelClassifier> _logger;
    private readonly string _modelRoot;
    private readonly object _stateLock     = new();
    private readonly object _inferenceLock = new();
    private readonly Dictionary<string, (SentinelResult R, DateTime At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prefetching = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingContext = new(StringComparer.OrdinalIgnoreCase);

    private InferenceSession?   _session;
    private WordPieceTokenizer? _tokenizer;
    private SentinelManifest?   _manifest;
    private string?             _modelDirectory;
    private bool                _loadAttempted;
    private string              _status = "not loaded";

    public SentinelClassifier(ILogger<SentinelClassifier>? logger = null, string? modelRoot = null)
    {
        _logger    = logger ?? NullLogger<SentinelClassifier>.Instance;
        _modelRoot = modelRoot ?? DefaultModelRoot();
    }

    /// <summary><c>%LOCALAPPDATA%\VELO\models\sentinel</c> (or the portable
    /// equivalent). Version sub-directories live underneath.</summary>
    public static string DefaultModelRoot()
        => Path.Combine(DataLocation.GetUserDataPath(), "models", "sentinel");

    /// <summary>The directory the loaded model came from, or null.</summary>
    public string? ModelDirectory { get { lock (_stateLock) return _modelDirectory; } }

    /// <summary>Manifest of the loaded model, or null when nothing is loaded.</summary>
    public SentinelManifest? Manifest { get { lock (_stateLock) return _manifest; } }

    public bool IsLoaded { get { lock (_stateLock) return _session is not null; } }

    /// <summary>One-line state for Settings → AI. Never throws, never null.</summary>
    public string Status { get { lock (_stateLock) return _status; } }

    public int CacheCount { get { lock (_stateLock) return _cache.Count; } }

    /// <summary>Where the model root is expected on this machine (shown in Settings).</summary>
    public string ModelRoot => _modelRoot;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Describes the model installed on disk WITHOUT loading it — reading the
    /// manifest costs a kilobyte, building the ONNX session costs 67 MB and
    /// ~130 ms, and a settings dialog has no business paying that. Returns a
    /// one-line status plus the directory (null when nothing is installed).
    /// </summary>
    public static (string Status, string? Directory) DescribeInstalledModel(string? modelRoot = null)
    {
        var root = modelRoot ?? DefaultModelRoot();
        try
        {
            var dir = LocateModelDirectory(root);
            if (dir is null) return ("not installed", null);

            var manifest = SentinelManifest.FromFile(Path.Combine(dir, ManifestFileName));
            var sizeMb   = new FileInfo(Path.Combine(dir, ModelFileName)).Length / (1024.0 * 1024.0);

            return manifest.IsSchemaSupported
                ? ($"{manifest.Model} v{manifest.Version} · {sizeMb:0.#} MB · block threshold {manifest.BlockThreshold:0.##}", dir)
                : ($"unsupported schema {manifest.Schema} (this build reads {SentinelManifest.MinSupportedSchema}-{SentinelManifest.MaxSupportedSchema})", dir);
        }
        catch (Exception ex)
        {
            return ($"unreadable: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Normalises whatever the caller has into the model's input contract:
    /// lowercase host, no scheme, no path, no port, no trailing dot, no
    /// userinfo. Returns "" when nothing usable is left.
    /// </summary>
    public static string NormalizeHost(string? hostOrUrl)
    {
        if (string.IsNullOrWhiteSpace(hostOrUrl)) return "";

        var s = hostOrUrl.Trim();

        var schemeIdx = s.IndexOf("//", StringComparison.Ordinal);
        if (schemeIdx >= 0) s = s[(schemeIdx + 2)..];

        var slash = s.IndexOf('/');
        if (slash >= 0) s = s[..slash];

        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];

        // IPv6 literals keep their brackets out of the model's way entirely —
        // an address is not a name, so there is nothing for the model to read.
        if (s.StartsWith('[')) return "";

        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];

        return s.TrimEnd('.').ToLowerInvariant();
    }

    /// <summary>
    /// Loads the model if it hasn't been tried yet. Returns false (and logs
    /// exactly once) when there is nothing to load or loading failed — the
    /// caller then treats every host as Allow.
    /// </summary>
    public bool EnsureLoaded()
    {
        lock (_stateLock)
        {
            if (_loadAttempted) return _session is not null;
            _loadAttempted = true;

            try
            {
                var dir = LocateModelDirectory(_modelRoot);
                if (dir is null)
                {
                    _status = "model not installed";
                    _logger.LogInformation(
                        "Sentinel disabled: no model under {Root}. Every host allowed until the model is installed.",
                        _modelRoot);
                    return false;
                }

                var manifest = SentinelManifest.FromFile(Path.Combine(dir, ManifestFileName));
                if (!manifest.IsSchemaSupported)
                {
                    _status = $"unsupported model schema {manifest.Schema}";
                    _logger.LogWarning(
                        "Sentinel disabled: model schema {Schema} outside supported range {Min}-{Max} ({Dir}).",
                        manifest.Schema, SentinelManifest.MinSupportedSchema, SentinelManifest.MaxSupportedSchema, dir);
                    return false;
                }

                var tokenizer = WordPieceTokenizer.FromFile(Path.Combine(dir, TokenizerFileName));

                var options = new SessionOptions
                {
                    IntraOpNumThreads      = 1,
                    InterOpNumThreads      = 1,
                    ExecutionMode          = ExecutionMode.ORT_SEQUENTIAL,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };

                var session = new InferenceSession(Path.Combine(dir, ModelFileName), options);

                _session        = session;
                _tokenizer      = tokenizer;
                _manifest       = manifest;
                _modelDirectory = dir;
                _status         = $"model-v{manifest.Version} (schema {manifest.Schema}, τ={manifest.BlockThreshold:0.##})";

                _logger.LogInformation(
                    "Sentinel loaded {Model} v{Version} (schema {Schema}, max_len {MaxLen}, block threshold {Threshold:0.##}) from {Dir}",
                    manifest.Model, manifest.Version, manifest.Schema, manifest.MaxLen, manifest.BlockThreshold, dir);
                return true;
            }
            catch (Exception ex)
            {
                _status = $"load failed: {ex.Message}";
                _logger.LogWarning(ex, "Sentinel disabled: model load failed under {Root}", _modelRoot);
                _session = null;
                return false;
            }
        }
    }

    /// <summary>
    /// S-D — drops the current session and re-runs discovery, so a model
    /// installed while VELO is running takes effect without a restart.
    /// Returns whether a model is loaded afterwards.
    ///
    /// The cache is cleared too: verdicts from the old model are not the new
    /// model's opinion, and keeping them would make the first minutes after an
    /// upgrade a silent mix of both.
    /// </summary>
    public bool Reload()
    {
        lock (_stateLock)
        {
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
            _manifest = null;
            _modelDirectory = null;
            _loadAttempted = false;
            _status = "not loaded";
            _cache.Clear();
        }

        _logger.LogInformation("Sentinel reloading from {Root}", _modelRoot);
        return EnsureLoaded();
    }

    /// <summary>
    /// Classifies <paramref name="hostOrUrl"/>, blocking on the inference
    /// (~10 ms with a loaded model). Call from async paths only — sync guards
    /// use <see cref="TryGetCachedVerdict"/> plus <see cref="Prefetch"/>.
    /// Never throws.
    /// </summary>
    public SentinelResult Classify(string? hostOrUrl)
    {
        var host = NormalizeHost(hostOrUrl);
        if (host.Length == 0)
            return SentinelResult.Unavailable with { Reason = "empty host" };

        var cached = TryGetCachedVerdict(host);
        if (cached is not null) return cached;

        if (!EnsureLoaded()) return SentinelResult.Unavailable;

        SentinelResult result;
        try
        {
            result = Infer(host);
        }
        catch (Exception ex)
        {
            // A throwing session is a broken model, not a threat signal.
            _logger.LogWarning(ex, "Sentinel inference failed for {Host}; allowing", host);
            return SentinelResult.Unavailable with { Reason = $"inference error: {ex.Message}" };
        }

        lock (_stateLock) { _cache[host] = (result, DateTime.UtcNow); }

        // Lesson #29 — every non-Allow verdict says what produced it, once per
        // host per TTL. In Shadow the line is the whole point of S-E: it is the
        // record the maintainer diffs against real field blocks.
        if (result.Action != SentinelAction.Allow)
        {
            string context;
            lock (_stateLock) { _pendingContext.TryGetValue(host, out context!); }

            _logger.LogInformation(
                "Sentinel {Mode} {Action} {Host} → {Label} p={Confidence:0.###} (τ={Threshold:0.##}) [{Context}]",
                Mode == SentinelMode.Shadow ? "SHADOW" : "ENFORCE",
                result.Action, host, result.Label, result.Confidence,
                _manifest?.BlockThreshold ?? 0, context ?? "direct");
        }

        return result;
    }

    /// <summary>Async wrapper — the inference runs on the thread pool so callers on the UI thread never stall.</summary>
    public Task<SentinelResult> ClassifyAsync(string? hostOrUrl, CancellationToken ct = default)
        => Task.Run(() => Classify(hostOrUrl), ct);

    /// <summary>
    /// Cached verdict for <paramref name="hostOrUrl"/>, or null when the host
    /// hasn't been classified within the TTL. The read sync guards use.
    /// </summary>
    public SentinelResult? TryGetCachedVerdict(string? hostOrUrl)
    {
        var host = NormalizeHost(hostOrUrl);
        if (host.Length == 0) return null;

        lock (_stateLock)
        {
            if (!_cache.TryGetValue(host, out var entry)) return null;
            if (DateTime.UtcNow - entry.At >= CacheTtl) { _cache.Remove(host); return null; }
            return entry.R with { FromCache = true };
        }
    }

    /// <summary>
    /// Fire-and-forget classification for a host a sync guard just saw and had
    /// no verdict for. Deduplicated per host so a page pulling 30 assets off
    /// one CDN queues one inference, not 30. Returns immediately.
    /// </summary>
    /// <param name="context">
    /// Optional description of the request that triggered this — "third-party
    /// Script", "main-frame Document". It rides along into the one-time verdict
    /// log so the shadow record is self-describing: without it, S-E cannot tell
    /// a third-party beacon from a top-level navigation when reading the field
    /// logs weeks later.
    /// </param>
    public void Prefetch(string? hostOrUrl, string? context = null)
    {
        var host = NormalizeHost(hostOrUrl);
        if (host.Length == 0) return;

        lock (_stateLock)
        {
            if (_loadAttempted && _session is null) return;   // no model — don't queue work forever
            if (_cache.ContainsKey(host)) return;
            if (!_prefetching.Add(host)) return;
            if (context is not null) _pendingContext[host] = context;
        }

        _ = Task.Run(() =>
        {
            try { Classify(host); }
            catch { /* Classify already fail-softs; this is belt-and-braces */ }
            finally
            {
                lock (_stateLock)
                {
                    _prefetching.Remove(host);
                    _pendingContext.Remove(host);
                }
            }
        });
    }

    public void ClearCache()
    {
        lock (_stateLock) { _cache.Clear(); }
    }

    /// <summary>
    /// Test seam — installs a verdict as if inference had produced it, so the
    /// guards' handling of each action can be exercised without a 67 MB model
    /// on disk. Never called from product code.
    /// </summary>
    internal void SeedVerdict(string host, SentinelResult result)
    {
        var key = NormalizeHost(host);
        if (key.Length == 0) return;
        lock (_stateLock) { _cache[key] = (result, DateTime.UtcNow); }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────

    /// <summary>
    /// Picks the model directory: the highest version sub-directory under
    /// <paramref name="root"/> that holds all three assets. Also accepts the
    /// assets sitting directly in the root, which is how you drop a release
    /// build in by hand for a runtime check before S-D lands the downloader.
    /// </summary>
    internal static string? LocateModelDirectory(string root)
    {
        if (!Directory.Exists(root)) return null;
        if (IsCompleteModelDir(root)) return root;

        return Directory.EnumerateDirectories(root)
            .Where(IsCompleteModelDir)
            .OrderByDescending(ParseVersion)
            .ThenByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsCompleteModelDir(string dir)
        => File.Exists(Path.Combine(dir, ModelFileName))
        && File.Exists(Path.Combine(dir, TokenizerFileName))
        && File.Exists(Path.Combine(dir, ManifestFileName));

    private static int ParseVersion(string dir)
    {
        var name = Path.GetFileName(dir).TrimStart('v', 'V');
        return int.TryParse(name, out var n) ? n : -1;
    }

    private SentinelResult Infer(string host)
    {
        InferenceSession   session;
        WordPieceTokenizer tokenizer;
        SentinelManifest   manifest;

        lock (_stateLock)
        {
            if (_session is null || _tokenizer is null || _manifest is null)
                return SentinelResult.Unavailable;
            session = _session; tokenizer = _tokenizer; manifest = _manifest;
        }

        var (ids, mask) = tokenizer.Encode(host, manifest.MaxLen);

        var idsTensor  = new DenseTensor<long>(ids,  [1, manifest.MaxLen]);
        var maskTensor = new DenseTensor<long>(mask, [1, manifest.MaxLen]);

        float[] logits;
        // Serialised: the session is single-threaded on purpose, and letting N
        // page-load prefetches run concurrently would hand the browser's cores
        // to the classifier.
        lock (_inferenceLock)
        {
            using var outputs = session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input_ids", idsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
            ]);

            logits = outputs.First().AsEnumerable<float>().ToArray();
        }

        var probs = Softmax(logits);
        return Decide(probs, manifest.BlockThreshold);
    }

    /// <summary>
    /// The decision rule, kept in lockstep with <c>evaluate.py</c>'s
    /// <c>decide()</c> — the gates (AUC 0.9907, benign FPR 0.74%, never-block
    /// 0 failures, must-catch 4/4) were measured under exactly this rule, so a
    /// divergence here invalidates them.
    /// </summary>
    internal static SentinelResult Decide(IReadOnlyList<float> probs, double blockThreshold)
    {
        if (probs.Count == 0)
            return SentinelResult.Unavailable with { Reason = "empty model output" };

        var best = 0;
        for (var i = 1; i < probs.Count; i++)
            if (probs[i] > probs[best]) best = i;

        var label = (SentinelLabel)best;
        var p     = (double)probs[best];

        if (label == SentinelLabel.Benign)
            return new SentinelResult(label, p, SentinelAction.Allow, "benign");

        if (p >= blockThreshold)
        {
            return new SentinelResult(label, p, SentinelAction.Block,
                $"sentinel classified host as {label.ToString().ToLowerInvariant()} (p={p:0.###} ≥ {blockThreshold:0.##})");
        }

        if (label == SentinelLabel.Phishing)
        {
            return new SentinelResult(label, p, SentinelAction.Flag,
                $"sentinel phishing signal below block threshold (p={p:0.###} < {blockThreshold:0.##})");
        }

        // A low-confidence tracker/ad guess adds nothing the blocklists don't
        // already do better, and it is the cheapest place to lose a user's trust.
        return new SentinelResult(SentinelLabel.Benign, p, SentinelAction.Allow,
            $"low-confidence {label.ToString().ToLowerInvariant()} guess ignored (p={p:0.###})");
    }

    internal static float[] Softmax(IReadOnlyList<float> logits)
    {
        var result = new float[logits.Count];
        if (logits.Count == 0) return result;

        var max = logits[0];
        for (var i = 1; i < logits.Count; i++) if (logits[i] > max) max = logits[i];

        double sum = 0;
        for (var i = 0; i < logits.Count; i++)
        {
            var e = Math.Exp(logits[i] - max);
            result[i] = (float)e;
            sum += e;
        }

        if (sum <= 0) return result;
        for (var i = 0; i < result.Length; i++) result[i] = (float)(result[i] / sum);
        return result;
    }
}
