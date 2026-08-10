using System.Globalization;

namespace VELO.Core.Media;

/// <summary>One entry of a master playlist: a quality the stream is offered in.</summary>
public sealed record HlsVariant(
    string Url,
    long   Bandwidth,
    string Resolution,
    string Codecs,
    string Name);

/// <summary>
/// A media playlist: the ordered segment list that actually gets fetched.
/// </summary>
/// <param name="InitSegmentUrl">
/// From <c>#EXT-X-MAP</c>, present on fMP4 streams and null on MPEG-TS. When
/// present it must be written FIRST and exactly once — it is the
/// initialisation segment, the same thing Gate 0.5 measured arriving as the
/// first append of every SourceBuffer.
/// </param>
/// <param name="IsComplete">
/// True when the playlist carried <c>#EXT-X-ENDLIST</c>, i.e. it is VOD and
/// the segment list is the whole asset. A live playlist has no end and is a
/// different problem.
/// </param>
public sealed record HlsMediaPlaylist(
    IReadOnlyList<string> SegmentUrls,
    string?               InitSegmentUrl,
    double                TotalSeconds,
    bool                  IsComplete);

/// <summary>
/// Phase 6 / P2a-2 — HLS manifest parsing.
///
/// Written against manifests captured from the real stream the phase has been
/// measuring (<c>test-streams.mux.dev</c>), not against the spec's examples:
/// the tests embed that actual text, tag ordering and all.
///
/// DASH is deliberately absent. Every measurement in this phase came from HLS
/// or from MSE; the dash.js reference player never loaded a stream in any
/// pass, so there is no measured MPD to write a parser against. Writing one
/// blind is the thing this phase keeps proving wrong.
/// </summary>
public static class HlsManifestParser
{
    /// <summary>
    /// A master playlist lists other playlists; a media playlist lists
    /// segments. The discriminator is which tag appears, not the filename —
    /// both are <c>.m3u8</c> and both were served as <c>audio/mpegurl</c>.
    /// </summary>
    public static bool IsMaster(string content) =>
        content.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal);

    /// <summary>
    /// Variants, highest bandwidth first. <paramref name="manifestUrl"/> is
    /// needed because the URIs are relative in the measured manifest
    /// (<c>url_0/…m3u8</c>) — resolving them against the document is not
    /// optional.
    /// </summary>
    public static IReadOnlyList<HlsVariant> ParseMaster(string content, string manifestUrl)
    {
        var variants = new List<HlsVariant>();
        if (string.IsNullOrWhiteSpace(content)) return variants;

        var lines = SplitLines(content);

        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;

            // The URI is the next non-comment, non-blank line. Skipping blanks
            // matters: the measured file has none, but hand-edited ones do.
            var uri = NextUri(lines, i + 1);
            if (uri is null) continue;

            var attributes = lines[i]["#EXT-X-STREAM-INF:".Length..];
            variants.Add(new HlsVariant(
                Url:        Resolve(manifestUrl, uri),
                Bandwidth:  ParseLong(Attribute(attributes, "BANDWIDTH")),
                Resolution: Attribute(attributes, "RESOLUTION"),
                Codecs:     Attribute(attributes, "CODECS"),
                Name:       Attribute(attributes, "NAME")));
        }

        return [.. variants.OrderByDescending(v => v.Bandwidth)];
    }

    /// <summary>Segments in playback order — the order is the file.</summary>
    public static HlsMediaPlaylist ParseMediaPlaylist(string content, string manifestUrl)
    {
        var segments = new List<string>();
        string? init  = null;
        var total     = 0.0;
        var complete  = false;

        if (string.IsNullOrWhiteSpace(content))
            return new HlsMediaPlaylist(segments, null, 0, false);

        var lines = SplitLines(content);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var uri = Attribute(line["#EXT-X-MAP:".Length..], "URI");
                if (!string.IsNullOrEmpty(uri)) init = Resolve(manifestUrl, uri);
                continue;
            }

            if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal))
            {
                complete = true;
                continue;
            }

            if (!line.StartsWith("#EXTINF:", StringComparison.Ordinal)) continue;

            // "#EXTINF:10.000," — duration up to the comma, invariant culture
            // because a Spanish locale would read "10.000" as ten thousand.
            var durationText = line["#EXTINF:".Length..].Split(',')[0].Trim();
            if (double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                total += seconds;

            var uriLine = NextUri(lines, i + 1);
            if (uriLine is not null) segments.Add(Resolve(manifestUrl, uriLine));
        }

        return new HlsMediaPlaylist(segments, init, total, complete);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<string> SplitLines(string content) =>
        [.. content.Split('\n').Select(l => l.Trim('\r', ' ', '\t'))];

    private static string? NextUri(List<string> lines, int from)
    {
        for (var i = from; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) continue;
            if (lines[i][0] == '#') return null;   // another tag — this entry has no URI
            return lines[i];
        }
        return null;
    }

    /// <summary>
    /// Reads ATTR=value or ATTR="value" out of an attribute list. Quoted
    /// values are handled first because CODECS contains commas
    /// (<c>CODECS="mp4a.40.2,avc1.64001f"</c>) and splitting on commas would
    /// cut it in half — measured in the real master playlist.
    /// </summary>
    private static string Attribute(string attributes, string name)
    {
        var key = name + "=";
        var at  = attributes.IndexOf(key, StringComparison.Ordinal);
        while (at > 0 && attributes[at - 1] is not (',' or ' '))
        {
            at = attributes.IndexOf(key, at + 1, StringComparison.Ordinal);
            if (at < 0) return "";
        }
        if (at < 0) return "";

        var value = attributes[(at + key.Length)..];

        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end < 0 ? value[1..] : value[1..end];
        }

        var comma = value.IndexOf(',');
        return (comma < 0 ? value : value[..comma]).Trim();
    }

    private static long ParseLong(string text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>
    /// Resolves a possibly-relative playlist URI against the manifest it came
    /// from. Absolute URIs pass through untouched.
    /// </summary>
    public static string Resolve(string manifestUrl, string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return Uri.TryCreate(new Uri(manifestUrl), uri, out var resolved) ? resolved.ToString() : uri;
    }
}
