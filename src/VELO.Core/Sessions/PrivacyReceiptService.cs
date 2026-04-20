using Microsoft.Extensions.Logging;
using VELO.Data;
using VELO.Data.Models;

namespace VELO.Core.Sessions;

/// <summary>
/// Manages per-tab privacy sessions and persists a PrivacyStats record
/// when a tab is closed. Fires ReceiptReady so the UI can show the toast.
/// </summary>
public class PrivacyReceiptService(VeloDatabase db, ILogger<PrivacyReceiptService> logger)
{
    private readonly VeloDatabase                    _db     = db;
    private readonly ILogger<PrivacyReceiptService>  _logger = logger;
    private readonly Dictionary<string, TabSession>  _sessions = new();

    public event Action<TabSession>? ReceiptReady;

    // ── Session lifecycle ────────────────────────────────────────────────────

    public void StartSession(string tabId, string url)
    {
        var domain = ExtractDomain(url);
        _sessions[tabId] = new TabSession
        {
            TabId  = tabId,
            Domain = domain,
            Url    = url,
        };
    }

    public void UpdateUrl(string tabId, string url)
    {
        if (!_sessions.TryGetValue(tabId, out var session)) return;
        session.Url    = url;
        session.Domain = ExtractDomain(url);
    }

    public void RecordTrackerBlocked(string tabId)
    {
        if (_sessions.TryGetValue(tabId, out var s)) s.TrackersBlocked++;
    }

    public void RecordAdBlocked(string tabId)
    {
        if (_sessions.TryGetValue(tabId, out var s)) s.AdsBlocked++;
    }

    public void RecordFingerprintBlocked(string tabId)
    {
        if (_sessions.TryGetValue(tabId, out var s)) s.FingerprintBlocked++;
    }

    public void RecordRequest(string tabId, bool blocked)
    {
        if (!_sessions.TryGetValue(tabId, out var s)) return;
        s.RequestsTotal++;
        if (blocked) s.RequestsBlocked++;
    }

    public void UpdateShieldScore(string tabId, int score, bool isGolden)
    {
        if (!_sessions.TryGetValue(tabId, out var s)) return;
        s.ShieldScore  = score;
        s.IsGoldenList = isGolden;
    }

    // ── Close tab — persist + notify ────────────────────────────────────────

    public async Task CloseTabAsync(string tabId)
    {
        if (!_sessions.Remove(tabId, out var session)) return;

        // Only persist if the session had real content
        if (string.IsNullOrEmpty(session.Domain) || session.Domain == "newtab")
            return;

        try
        {
            var stats = new PrivacyStats
            {
                TabId              = session.TabId,
                Domain             = session.Domain,
                Url                = session.Url,
                TrackersBlocked    = session.TrackersBlocked,
                AdsBlocked         = session.AdsBlocked,
                FingerprintBlocked = session.FingerprintBlocked,
                RequestsTotal      = session.RequestsTotal,
                RequestsBlocked    = session.RequestsBlocked,
                IsGoldenList       = session.IsGoldenList,
                ShieldScore        = session.ShieldScore,
                SessionStart       = session.SessionStart,
                SessionEnd         = DateTime.UtcNow,
                DurationSeconds    = session.DurationSeconds,
            };

            await _db.Connection.InsertAsync(stats);
            _logger.LogDebug("Privacy receipt saved for {Domain} ({Trackers} trackers blocked)", session.Domain, session.TrackersBlocked);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist privacy receipt for tab {TabId}", tabId);
        }

        ReceiptReady?.Invoke(session);
    }

    public TabSession? GetSession(string tabId)
        => _sessions.TryGetValue(tabId, out var s) ? s : null;

    // ── Aggregate stats (for Privacy Receipt dialog) ─────────────────────────

    public async Task<(int Trackers, int Ads, int Fingerprints)> GetLifetimeTotalsAsync()
    {
        var trackers     = await _db.Connection.ExecuteScalarAsync<int>("SELECT SUM(TrackersBlocked) FROM privacy_stats");
        var ads          = await _db.Connection.ExecuteScalarAsync<int>("SELECT SUM(AdsBlocked) FROM privacy_stats");
        var fingerprints = await _db.Connection.ExecuteScalarAsync<int>("SELECT SUM(FingerprintBlocked) FROM privacy_stats");
        return (trackers, ads, fingerprints);
    }

    private static string ExtractDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        return uri.Host.TrimStart('w', '.');
    }
}
