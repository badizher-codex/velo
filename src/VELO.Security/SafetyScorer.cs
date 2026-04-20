using VELO.Security.AI.Models;
using VELO.Security.GoldenList;
using VELO.Security.Models;

namespace VELO.Security;

/// <summary>
/// Computes a SafetyResult from a SafetyContext.
///
/// Algorithm (executed in order):
///   1. Short-circuit rules — immediately return Red if any apply.
///   2. Incremental scoring — accumulate score from all signals.
///   3. Bucketing — map numeric score to SafetyLevel.
/// </summary>
public class SafetyScorer(IGoldenList goldenList)
{
    private readonly IGoldenList _goldenList = goldenList;

    public SafetyResult Compute(SafetyContext ctx)
    {
        // ── 1. Short-circuit rules (hard Red) ────────────────────────────────
        foreach (var verdict in ctx.SessionVerdicts)
        {
            if (verdict.Verdict == VerdictType.Block)
                return SafetyResult.Red($"Amenaza bloqueada: {verdict.Reason}");
        }

        if (ctx.TLSStatus == TLSStatus.Http)
            return SafetyResult.Red("La página no usa HTTPS. Tu conexión no está cifrada.");

        if (ctx.TLSStatus == TLSStatus.Expired)
            return SafetyResult.Red("El certificado TLS ha expirado.");

        // ── 2. Incremental scoring ────────────────────────────────────────────
        var positives = new List<string>();
        var negatives = new List<string>();
        int score = 0;

        // Golden List bonus
        if (ctx.IsGoldenList)
        {
            score += 40;
            positives.Add("Dominio en la Golden List: conocido por buenas prácticas de privacidad.");
        }

        // Whitelisted by user
        if (ctx.IsWhitelistedByUser)
        {
            score += 20;
            positives.Add("Marcado como de confianza por ti.");
        }

        // TLS signals
        switch (ctx.TLSStatus)
        {
            case TLSStatus.Valid:
                score += 20;
                positives.Add("Conexión TLS válida.");
                break;
            case TLSStatus.SelfSigned:
                score -= 15;
                negatives.Add("Certificado auto-firmado (no verificado por autoridad pública).");
                break;
            case TLSStatus.NoCtLogs:
                score -= 15;
                negatives.Add("Certificado no encontrado en logs de transparencia (CT).");
                break;
            case TLSStatus.Error:
                score -= 15;
                negatives.Add("Error de TLS desconocido.");
                break;
        }

        // AI verdict
        if (ctx.AIVerdict is { } ai)
        {
            switch (ai.Verdict)
            {
                case VerdictType.Block:
                    score -= 50;
                    negatives.Add($"IA detectó una amenaza: {ai.Reason}");
                    break;
                case VerdictType.Warn:
                    score -= 20;
                    negatives.Add($"IA advirtió comportamiento sospechoso: {ai.Reason}");
                    break;
                case VerdictType.Safe:
                    score += 10;
                    positives.Add("IA evaluó la página como segura.");
                    break;
            }
        }

        // Session Warn verdicts
        int warnCount = ctx.SessionVerdicts.Count(v => v.Verdict == VerdictType.Warn);
        if (warnCount > 0)
        {
            score -= warnCount * 10;
            negatives.Add($"{warnCount} advertencia(s) de seguridad en esta sesión.");
        }

        // Trackers blocked (positive — they were blocked)
        if (ctx.TrackersBlockedCount > 0)
        {
            int bonus = Math.Min(ctx.TrackersBlockedCount * 2, 15);
            score += bonus;
            positives.Add($"{ctx.TrackersBlockedCount} rastreador(es) bloqueado(s).");
        }

        // Fingerprint attempts blocked
        if (ctx.FingerprintAttemptsBlocked > 0)
        {
            score -= Math.Min(ctx.FingerprintAttemptsBlocked * 5, 20);
            negatives.Add($"{ctx.FingerprintAttemptsBlocked} intento(s) de fingerprinting detectado(s).");
        }

        // Clamp to [-100, +100]
        score = Math.Clamp(score, -100, 100);

        // ── 3. Bucketing ──────────────────────────────────────────────────────
        SafetyLevel level;
        if (ctx.IsGoldenList && score >= 40)
            level = SafetyLevel.Gold;
        else if (score >= 20)
            level = SafetyLevel.Green;
        else if (score >= -15)
            level = SafetyLevel.Yellow;
        else
            level = SafetyLevel.Red;

        return new SafetyResult(
            level,
            score,
            positives.AsReadOnly(),
            negatives.AsReadOnly(),
            null,
            DateTime.UtcNow);
    }
}
