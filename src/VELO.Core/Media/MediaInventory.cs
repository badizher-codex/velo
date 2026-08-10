namespace VELO.Core.Media;

/// <summary>A directly fetchable media file seen on the network.</summary>
public sealed record ProgressiveItem(string Url, string ContentType, long TotalBytes);

/// <summary>What a row in the media panel is.</summary>
public enum MediaOfferKind { Protected, ProgressiveFile, AudioTrack, VideoTrack, Manifest }

/// <summary>
/// One row the panel renders. Built by <see cref="MediaInventory.BuildOffers"/>
/// so the decisions — what is offered, what is refused, and why — are a pure
/// function with tests, rather than logic living in XAML code-behind.
///
/// <paramref name="BlockedReason"/> is never null when
/// <paramref name="CanDownload"/> is false. A row the user cannot act on must
/// say why: a disabled control with no explanation is the failure mode P4 was
/// written to avoid.
/// </summary>
public sealed record MediaOffer(
    MediaOfferKind Kind,
    string  Title,
    string  Detail,
    string? Url,
    bool    CanDownload,
    string? BlockedReason);

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

    /// <summary>
    /// Turns the inventory into the rows the panel shows.
    ///
    /// Order of decisions matters and is deliberate:
    ///
    ///   1. Protected content short-circuits everything. If the page is
    ///      actually using encryption, no row is offered at all — offering a
    ///      download that would produce encrypted, unplayable bytes is worse
    ///      than offering nothing, and §5 settles that we decline rather than
    ///      attempt.
    ///   2. Progressive files are offered for real; the engine handles them today.
    ///   3. Adaptive tracks and manifests are listed with an explicit reason,
    ///      never as a dead control. The user can see VELO found the audio and
    ///      the video separately even while neither can be fetched yet.
    /// </summary>
    public IReadOnlyList<MediaOffer> BuildOffers()
    {
        lock (_lock)
        {
            if (MediaClassifier.IsProtected(_page.Drm))
            {
                return
                [
                    new MediaOffer(
                        MediaOfferKind.Protected,
                        "Protected content",
                        "This page plays DRM-protected media. VELO does not download it.",
                        null, false,
                        "Protected content cannot be downloaded."),
                ];
            }

            var offers = new List<MediaOffer>();

            foreach (var item in _progressive.Values.OrderByDescending(p => p.TotalBytes))
            {
                offers.Add(new MediaOffer(
                    MediaOfferKind.ProgressiveFile,
                    FileNameFor(item.Url),
                    $"{item.ContentType} · {FormatBytes(item.TotalBytes)}",
                    item.Url, true, null));
            }

            foreach (var track in _page.Tracks)
            {
                var kind = track.Kind == TrackKind.Audio
                    ? MediaOfferKind.AudioTrack
                    : MediaOfferKind.VideoTrack;

                offers.Add(new MediaOffer(
                    kind,
                    track.Kind == TrackKind.Audio ? "Audio track" : "Video track",
                    $"{(string.IsNullOrEmpty(track.Codecs) ? track.Mime : track.Codecs)} · {FormatBytes(track.Bytes)} buffered",
                    null, false,
                    "This stream is assembled inside the page. Capturing it is not implemented yet."));
            }

            foreach (var manifest in _manifests.Values)
            {
                offers.Add(new MediaOffer(
                    MediaOfferKind.Manifest,
                    manifest.Kind == MediaClass.HlsManifest ? "HLS stream" : "DASH stream",
                    FileNameFor(manifest.Url),
                    manifest.Url, false,
                    "Segmented downloads are not implemented yet."));
            }

            return offers;
        }
    }

    /// <summary>Last path segment, or the host when there is none.</summary>
    public static string FileNameFor(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

        var name = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : "";
        return string.IsNullOrEmpty(name) ? uri.Host : Uri.UnescapeDataString(name);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B",
    };

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
