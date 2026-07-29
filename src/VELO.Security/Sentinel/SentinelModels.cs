using System.Text.Json;

namespace VELO.Security.Sentinel;

/// <summary>Classes the model was trained on, in the order of its logits.</summary>
public enum SentinelLabel
{
    Benign   = 0,
    Phishing = 1,
    Tracker  = 2,
    Ad       = 3,
}

/// <summary>
/// The two-level verdict semantics fixed during S-B and recorded in the
/// model manifest (<c>semantics</c> field):
///
/// • <see cref="Block"/> — argmax is not benign AND its probability clears
///   <c>conf_threshold_block</c>. This is the only level allowed to stop a
///   request, and only in <see cref="SentinelMode.Enforce"/>.
/// • <see cref="Flag"/> — argmax is phishing below the threshold. A signal,
///   never a block on its own: it is handed to PhishingShield, which weighs
///   it together with TLS, domain age, login-form presence and the host
///   heuristics.
/// • <see cref="Allow"/> — everything else, including a low-confidence
///   tracker/ad guess (the static blocklists own that job; a 0.6-confidence
///   guess adds nothing but false positives).
/// </summary>
public enum SentinelAction { Allow, Flag, Block }

/// <summary>
/// Whether Sentinel's verdicts are applied or only recorded. Default is
/// <see cref="Shadow"/> — S-E ships one release that only logs, so the
/// verdicts can be diffed against the maintainer's field logs before
/// anything is allowed to cancel a request (lesson #30).
/// </summary>
public enum SentinelMode { Shadow, Enforce }

/// <summary>
/// One classification. <paramref name="Reason"/> is always populated for
/// non-Allow actions so every applied verdict can say what produced it
/// (lesson #29).
/// </summary>
public sealed record SentinelResult(
    SentinelLabel  Label,
    double         Confidence,
    SentinelAction Action,
    string         Reason,
    bool           FromCache = false)
{
    public static readonly SentinelResult Unavailable =
        new(SentinelLabel.Benign, 0, SentinelAction.Allow, "sentinel model not loaded");
}

/// <summary>
/// The subset of <c>manifest.json</c> (published alongside the .onnx in the
/// <c>model-vN</c> release) that the runtime depends on. Thresholds and the
/// label order travel with the model — hard-coding them here would mean a
/// retrained model silently running under the old operating point.
/// </summary>
public sealed record SentinelManifest(
    string   Model,
    int      Version,
    int      Schema,
    int      MaxLen,
    double   BlockThreshold,
    string[] Labels)
{
    /// <summary>Schema range this build knows how to run. A model published with
    /// a schema outside the range is refused (and logged) rather than guessed at,
    /// so a future input contract can't be fed to today's tokenizer.</summary>
    public const int MinSupportedSchema = 1;
    public const int MaxSupportedSchema = 1;

    public bool IsSchemaSupported => Schema is >= MinSupportedSchema and <= MaxSupportedSchema;

    public static SentinelManifest FromFile(string path) => FromJson(File.ReadAllText(path));

    public static SentinelManifest FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string Str(string name, string fallback)
            => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? fallback : fallback;

        int Int(string name, int fallback)
            => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32() : fallback;

        double Dbl(string name, double fallback)
            => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetDouble() : fallback;

        var labels = root.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : ["benign", "phishing", "tracker", "ad"];

        return new SentinelManifest(
            Model:          Str("model", "velo-sentinel"),
            Version:        Int("version", 0),
            Schema:         Int("schema", 0),
            MaxLen:         Int("max_len", 32),
            BlockThreshold: Dbl("conf_threshold_block", 0.85),
            Labels:         labels);
    }
}
