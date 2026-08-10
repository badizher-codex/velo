using System.Text.Json.Nodes;

namespace VELO.Core.Media;

/// <summary>One SourceBuffer, as the page sees it.</summary>
public sealed record MediaTrack(
    int       Index,
    string    Mime,
    TrackKind Kind,
    string    Codecs,
    string    Container,
    int       Appends,
    long      Bytes,
    bool      Encrypted);

/// <summary>A media element on the page.</summary>
/// <param name="SrcKind">
/// <c>blob</c>, <c>http</c>, <c>other</c> or <c>none</c>. A blob src means the
/// real addresses exist only on the network layer — measured on both YouTube
/// and the hls.js demo.
/// </param>
public sealed record MediaElementInfo(string Tag, string SrcKind, int DurationSeconds);

/// <summary>
/// The parsed form of one <c>media-detect.js</c> report.
///
/// The field names below are a contract with that script. A smoke test pins
/// both sides, because the failure mode is silent: the host swallows parse
/// errors and a renamed field just produces an empty inventory forever.
/// </summary>
public sealed record MediaPageReport(
    string                        Url,
    IReadOnlyList<MediaTrack>     Tracks,
    DrmSignals                    Drm,
    IReadOnlyList<MediaElementInfo> Elements)
{
    public static readonly MediaPageReport Empty =
        new("", [], new DrmSignals(), []);

    /// <summary>
    /// Parses a <c>kind: "media-detect"</c> payload. Returns false on anything
    /// unexpected rather than throwing — this runs on the UI thread inside the
    /// WebMessage handler, and a page can post whatever it likes.
    /// </summary>
    public static bool TryParse(string json, out MediaPageReport report)
    {
        report = Empty;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return false;
            if (root["kind"]?.GetValue<string>() != "media-detect") return false;

            var tracks = new List<MediaTrack>();
            if (root["buffers"] is JsonArray buffers)
            {
                foreach (var node in buffers)
                {
                    if (node is not JsonObject b) continue;

                    var mime = b["mime"]?.GetValue<string>() ?? "";
                    tracks.Add(new MediaTrack(
                        Index:     b["i"]?.GetValue<int>() ?? tracks.Count,
                        Mime:      mime,
                        Kind:      MediaClassifier.ClassifyTrack(mime),
                        Codecs:    MediaClassifier.ExtractCodecs(mime),
                        Container: b["first"]?["container"]?.GetValue<string>() ?? "unknown",
                        Appends:   b["appends"]?.GetValue<int>() ?? 0,
                        Bytes:     b["bytes"]?.GetValue<long>() ?? 0,
                        Encrypted: b["encrypted"]?.GetValue<bool>() ?? false));
                }
            }

            var eme = root["eme"] as JsonObject;
            var drm = new DrmSignals(
                KeySystemsProbed:   (eme?["probed"]   as JsonArray)?.Count ?? 0,
                KeySystemsResolved: (eme?["resolved"] as JsonArray)?.Count ?? 0,
                SetMediaKeysCalls:  eme?["setMediaKeys"]?.GetValue<int>() ?? 0,
                EncryptedEvents:    eme?["encryptedEvents"]?.GetValue<int>() ?? 0,
                // Encryption boxes are reported per track; the page-level
                // verdict is "any track carries them".
                InitSegmentHasPssh: AnyTrackFlag(root, "pssh"),
                InitSegmentHasSinf: AnyTrackFlag(root, "sinf"));

            var elements = new List<MediaElementInfo>();
            if (root["elements"] is JsonArray els)
            {
                foreach (var node in els)
                {
                    if (node is not JsonObject el) continue;
                    elements.Add(new MediaElementInfo(
                        el["tag"]?.GetValue<string>()     ?? "",
                        el["srcKind"]?.GetValue<string>() ?? "none",
                        el["duration"]?.GetValue<int>()   ?? 0));
                }
            }

            report = new MediaPageReport(
                root["url"]?.GetValue<string>() ?? "",
                tracks,
                drm,
                elements);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool AnyTrackFlag(JsonObject root, string flag)
    {
        if (root["buffers"] is not JsonArray buffers) return false;
        foreach (var node in buffers)
        {
            if (node is JsonObject b && b["first"]?[flag]?.GetValue<bool>() == true)
                return true;
        }
        return false;
    }
}
