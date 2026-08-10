namespace VELO.Core.Media;

/// <summary>A directly fetchable media file seen on the network.</summary>
public sealed record ProgressiveItem(string Url, string ContentType, long TotalBytes);

/// <summary>An adaptive-streaming manifest seen on the network.</summary>
public sealed record ManifestItem(string Url, MediaClass Kind);

/// <summary>
/// What one tab is currently playing or offering, read-only.
///
/// Two evidence sources feed this, and §9–§10 of the analysis established
/// that neither is sufficient alone:
///
///   • the network layer settles the progressive case and finds manifests;
///   • the page layer settles adaptive streams — it is the ONLY thing that
///     sees YouTube, whose stream is one opaque application/vnd.yt-ump
///     connection, and it is where the audio/video split actually lives.
///
/// Reset on navigation: an inventory that outlives its page describes the
/// wrong thing, and the URL bar would offer the previous video's tracks.
/// </summary>
public sealed class MediaInventory
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ProgressiveItem> _progressive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManifestItem>    _manifests   = new(StringComparer.Ordinal);
    private MediaPageReport _page = MediaPageReport.Empty;

    /// <summary>Directly fetchable files, above the content-size floor.</summary>
    public IReadOnlyList<ProgressiveItem> Progressive
    {
        get { lock (_lock) return [.. _progressive.Values]; }
    }

    /// <summary>HLS/DASH manifests seen. Segment provenance parsing is P2.</summary>
    public IReadOnlyList<ManifestItem> Manifests
    {
        get { lock (_lock) return [.. _manifests.Values]; }
    }

    /// <summary>The adaptive tracks the page is feeding to MSE.</summary>
    public IReadOnlyList<MediaTrack> Tracks
    {
        get { lock (_lock) return _page.Tracks; }
    }

    /// <summary>
    /// True when the page is actually using encryption — not merely probing
    /// for it. See <see cref="MediaClassifier.IsProtected"/>.
    /// </summary>
    public bool IsProtected
    {
        get { lock (_lock) return MediaClassifier.IsProtected(_page.Drm); }
    }

    /// <summary>Nothing found worth offering.</summary>
    public bool IsEmpty
    {
        get { lock (_lock) return _progressive.Count == 0 && _manifests.Count == 0 && _page.Tracks.Count == 0; }
    }

    /// <summary>
    /// Feeds one network response. Ignores everything that is not media, which
    /// is the overwhelming majority — 765 responses in the reference capture
    /// produced three progressive entries and six manifests.
    /// </summary>
    /// <returns>
    /// The class actually recorded, or <see cref="MediaClass.NotMedia"/> when
    /// the response contributed nothing. Callers use this to avoid logging or
    /// re-rendering on the ~99% of responses that are not media.
    /// </returns>
    public MediaClass RecordResponse(ResponseSignals signals)
    {
        var mediaClass = MediaClassifier.ClassifyResponse(signals);
        if (mediaClass == MediaClass.NotMedia) return MediaClass.NotMedia;

        var total = MediaClassifier.ResolveTotalBytes(signals);

        lock (_lock)
        {
            switch (mediaClass)
            {
                case MediaClass.ProgressiveMedia:
                    // The size floor lives here rather than in the classifier
                    // so that "it is an audio file" and "it is worth showing"
                    // stay separable — the four YouTube UI beeps are the first
                    // and not the second.
                    if (!MediaClassifier.IsUserContent(mediaClass, total)) return MediaClass.NotMedia;

                    // Ranged fetches deliver the same URL repeatedly; keep the
                    // entry with the largest known total.
                    if (_progressive.TryGetValue(signals.Url, out var existing) &&
                        existing.TotalBytes >= total)
                        return MediaClass.NotMedia;

                    _progressive[signals.Url] = new ProgressiveItem(
                        signals.Url, MediaClassifier.NormalizeContentType(signals.ContentType), total);
                    break;

                case MediaClass.HlsManifest:
                case MediaClass.DashManifest:
                    _manifests[signals.Url] = new ManifestItem(signals.Url, mediaClass);
                    break;
            }
        }

        return mediaClass;
    }

    /// <summary>
    /// Replaces the page-side view. Reports are cumulative snapshots from
    /// media-detect.js (append counts and byte totals only grow), so the
    /// latest one always supersedes its predecessor.
    /// </summary>
    public void ApplyPageReport(MediaPageReport report)
    {
        lock (_lock) _page = report;
    }

    /// <summary>Drops everything. Call on navigation.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _progressive.Clear();
            _manifests.Clear();
            _page = MediaPageReport.Empty;
        }
    }

    /// <summary>One-line summary for the measurement log and, later, the panel.</summary>
    public string Describe()
    {
        lock (_lock)
        {
            var tracks = string.Join(", ", _page.Tracks.Select(t =>
                $"{t.Kind}:{t.Codecs} {t.Appends}app/{t.Bytes}B{(t.Encrypted ? " ENCRYPTED" : "")}"));

            return $"progressive={_progressive.Count} manifests={_manifests.Count} " +
                   $"tracks=[{tracks}] protected={MediaClassifier.IsProtected(_page.Drm)}";
        }
    }
}
