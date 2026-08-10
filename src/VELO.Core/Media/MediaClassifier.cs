namespace VELO.Core.Media;

/// <summary>What a network response is, as far as the network layer can tell.</summary>
public enum MediaClass
{
    NotMedia,
    /// <summary>A complete media file at one URL. Fetchable directly.</summary>
    ProgressiveMedia,
    HlsManifest,
    DashManifest,
    /// <summary>
    /// Indistinguishable from any other binary by its headers; it counts as a
    /// segment only because a manifest we already parsed named it.
    /// </summary>
    Segment,
}

/// <summary>Which half of an adaptive stream a SourceBuffer carries.</summary>
public enum TrackKind { Unknown, Audio, Video }

/// <summary>The headers one response arrived with.</summary>
public sealed record ResponseSignals(
    string Url,
    string ContentType,
    string ContentLength = "",
    string ContentRange  = "");

/// <summary>
/// Everything the page observed about encryption. Deliberately keeps probes
/// and use in separate fields — conflating them is the exact mistake P0 caught.
/// </summary>
public sealed record DrmSignals(
    int KeySystemsProbed     = 0,
    int KeySystemsResolved   = 0,
    int SetMediaKeysCalls    = 0,
    int EncryptedEvents      = 0,
    bool InitSegmentHasPssh  = false,
    bool InitSegmentHasSinf  = false);

/// <summary>
/// Classifies media from the two independent evidence sources VELO has:
/// response headers (network) and SourceBuffer MIME strings (page).
///
/// Every rule here comes from a measurement recorded in
/// docs/Phase6/MEDIA_DOWNLOAD_ANALYSIS.md §9–§10, not from what the formats
/// are supposed to do. The three that cost the most to learn:
///
///   • URL extensions identify nothing. 370 requests, zero .mp4/.m3u8/.mpd/.m4s.
///   • Content-Type identifies exactly one case. It calls an HLS video
///     manifest "audio/mpegurl", calls HLS segments "application/octet-stream"
///     (so does Font Awesome), and calls YouTube's stream
///     "application/vnd.yt-ump", which is in no registry.
///   • A page probing key systems is not playing protected content.
/// </summary>
public static class MediaClassifier
{
    /// <summary>
    /// Below this, a media file is site furniture rather than something the
    /// user is watching. Measured: YouTube's four UI sound effects are
    /// 6.1–7.0 KB, the smallest real content in the capture was 788 KB.
    ///
    /// This is a heuristic and it is here rather than buried in a caller so it
    /// can be argued with. The principled discriminator — is this attached to
    /// a media element the user can see — needs the page layer, which only
    /// covers adaptive streams today.
    /// </summary>
    public const long MinimumContentBytes = 100_000;

    /// <summary>
    /// Strips parameters and normalises case: "Audio/MPEGURL; charset=utf-8"
    /// becomes "audio/mpegurl". Every rule below matches against this form,
    /// because the measured headers carry charset parameters at random.
    /// </summary>
    public static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return "";
        var semi = contentType.IndexOf(';');
        var bare = semi >= 0 ? contentType[..semi] : contentType;
        return bare.Trim().ToLowerInvariant();
    }

    // Measured on test-streams.mux.dev: the master and variant playlists both
    // answer "audio/mpegurl" for an H.264+AAC video stream. The standard type
    // is application/vnd.apple.mpegurl; real servers use all of these, and the
    // "audio" in two of them is a lie about the content, not a track kind.
    private static readonly HashSet<string> HlsManifestTypes = new(StringComparer.Ordinal)
    {
        "application/vnd.apple.mpegurl",
        "application/x-mpegurl",
        "audio/mpegurl",
        "audio/x-mpegurl",
    };

    /// <param name="referencedByManifest">
    /// True when a manifest already fetched on this page names this URL. This
    /// is the ONLY thing that makes a segment identifiable — its own headers
    /// never will.
    /// </param>
    public static MediaClass ClassifyResponse(ResponseSignals signals, bool referencedByManifest = false)
    {
        var type = NormalizeContentType(signals.ContentType);

        // Manifests first: two of the four HLS types start with "audio/", so
        // testing for audio before manifests would classify a video playlist
        // as an audio track and offer an audio-only download of a video.
        if (HlsManifestTypes.Contains(type)) return MediaClass.HlsManifest;
        if (type is "application/dash+xml" or "video/vnd.mpeg.dash.mpd") return MediaClass.DashManifest;

        // Provenance beats headers. octet-stream is shared with fonts, so it
        // is only a segment when a manifest vouched for it — and a manifest's
        // word is good regardless of what the segment claims to be.
        if (referencedByManifest) return MediaClass.Segment;

        if (type.StartsWith("video/", StringComparison.Ordinal) ||
            type.StartsWith("audio/", StringComparison.Ordinal))
            return MediaClass.ProgressiveMedia;

        // Last resort, and ONLY when the authoritative signal is missing
        // entirely — see ManifestFromExtension for why this is not a
        // re-admission of the rule §9 demolished.
        if (type.Length == 0)
        {
            var byExtension = ManifestFromExtension(signals.Url);
            if (byExtension is { } manifest) return manifest;
        }

        // Everything else, explicitly including application/octet-stream and
        // vendor types like application/vnd.yt-ump. YouTube's stream is not
        // recognisable here by design — the page layer handles it.
        return MediaClass.NotMedia;
    }

    /// <summary>
    /// Manifest class from the URL path, used ONLY when the response carried
    /// no Content-Type at all.
    ///
    /// Why this is not the extension rule §9 demolished: that rule ran
    /// *instead* of the headers and lost to every site. This one runs only
    /// when there are no headers to lose to. Measured cause — a warm cache:
    /// <c>WebResourceResponseReceived</c> surfaces cache hits with no
    /// Content-Type, Content-Length or Content-Range, and the HLS manifests
    /// that had classified correctly on a cold load silently stopped being
    /// found on the second visit.
    ///
    /// The scope is manifests and nothing else, and the measurement is why.
    /// Of 285 responses observed with no Content-Type:
    ///
    ///   • 184 had no extension at all — including YouTube's videoplayback,
    ///     so this cannot resurrect the failure P0 found;
    ///   • 44 were .js, plus .css/.ico/.jpg/.png/.html — none of them media;
    ///   • 33 were .ts. Excluded deliberately: a segment is media because a
    ///     manifest named it (§9), never because of its name, and .ts is also
    ///     the TypeScript extension — on a dev site those are source files;
    ///   • 8 were .mp3, and every one of them was a YouTube UI sound effect
    ///     (open/success/failure/no_input). Those four beeps have now turned
    ///     up as a false positive under three separate rules; they are not
    ///     getting a fourth;
    ///   • 3 were .m3u8 — the manifests this exists to recover.
    /// </summary>
    private static MediaClass? ManifestFromExtension(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // Query strings routinely carry tokens, and a signed manifest URL is
        // normal — compare the path only.
        var path = url.Split('?')[0].Split('#')[0];

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) return MediaClass.HlsManifest;
        if (path.EndsWith(".mpd",  StringComparison.OrdinalIgnoreCase)) return MediaClass.DashManifest;

        return null;
    }

    /// <summary>
    /// Whether a classified response is worth showing the user, as opposed to
    /// a UI beep or a thumbnail-sized clip. Separate from classification so
    /// the size rule can be changed without touching the type rules.
    /// </summary>
    public static bool IsUserContent(MediaClass mediaClass, long bytes) =>
        mediaClass switch
        {
            MediaClass.ProgressiveMedia => bytes >= MinimumContentBytes,
            MediaClass.HlsManifest or MediaClass.DashManifest => true,
            _ => false,
        };

    /// <summary>
    /// Total size of the resource, from Content-Range when present and
    /// Content-Length otherwise. Range-driven responses report only the slice
    /// in Content-Length, so reading that alone under-reports badly — the
    /// measured w3schools response said 788493 in both, but a partial fetch
    /// would not.
    /// </summary>
    public static long ResolveTotalBytes(ResponseSignals signals)
    {
        var range = signals.ContentRange;
        if (!string.IsNullOrWhiteSpace(range))
        {
            var slash = range.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < range.Length &&
                long.TryParse(range[(slash + 1)..].Trim(), out var total))
                return total;
        }

        return long.TryParse(signals.ContentLength?.Trim(), out var len) ? len : 0;
    }

    /// <summary>
    /// Track kind from a SourceBuffer MIME string. This is where the audio /
    /// video split actually comes from — measured live as two separate
    /// SourceBuffers on both YouTube and hls.js.
    /// </summary>
    public static TrackKind ClassifyTrack(string? sourceBufferMime)
    {
        var type = NormalizeContentType(sourceBufferMime);
        // A manifest type is never a track. A SourceBuffer should never be
        // created with one, but two of the HLS types begin with "audio/" and
        // that trap has already been sprung once — see ClassifyResponse.
        if (HlsManifestTypes.Contains(type)) return TrackKind.Unknown;
        if (type.StartsWith("audio/", StringComparison.Ordinal)) return TrackKind.Audio;
        if (type.StartsWith("video/", StringComparison.Ordinal)) return TrackKind.Video;
        return TrackKind.Unknown;
    }

    /// <summary>
    /// Pulls the codec list out of a SourceBuffer MIME string:
    /// <c>audio/webm; codecs="opus"</c> yields <c>opus</c>. Returns empty when
    /// absent rather than guessing.
    /// </summary>
    public static string ExtractCodecs(string? sourceBufferMime)
    {
        if (string.IsNullOrWhiteSpace(sourceBufferMime)) return "";

        var idx = sourceBufferMime.IndexOf("codecs", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";

        var eq = sourceBufferMime.IndexOf('=', idx);
        if (eq < 0) return "";

        var value = sourceBufferMime[(eq + 1)..].Trim();
        var end = value.IndexOf(';');
        if (end >= 0) value = value[..end];

        return value.Trim().Trim('"', '\'').Trim();
    }

    /// <summary>
    /// Whether the page is playing protected content.
    ///
    /// The rule is USE, never capability. bitmovin's demo probes 13 key
    /// systems on load with nothing protected playing (P0), so a rule keyed on
    /// requestMediaKeySystemAccess — or even on it resolving — would refuse
    /// downloads on any site that merely feature-detects. What counts is the
    /// page actually attaching keys, an encrypted event firing, or the
    /// initialisation segment carrying encryption boxes.
    /// </summary>
    public static bool IsProtected(DrmSignals signals) =>
        signals.SetMediaKeysCalls   > 0 ||
        signals.EncryptedEvents     > 0 ||
        signals.InitSegmentHasPssh  ||
        signals.InitSegmentHasSinf;
}
