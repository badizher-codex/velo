using System.Text.Json;
using VELO.Security.AI.Models;

namespace VELO.Security.AI.Adapters;

internal static class VerdictParser
{
    internal static AIVerdict Parse(string json, string source)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var verdict     = root.GetProperty("verdict").GetString() ?? "SAFE";
            var confidence  = root.TryGetProperty("confidence",   out var c) ? c.GetInt32()    : 50;
            var reason      = root.TryGetProperty("reason",       out var r) ? r.GetString() ?? "" : "";
            var threatStr   = root.TryGetProperty("threat_type",  out var t) ? t.GetString()   : null;

            var threatType = threatStr switch
            {
                "Tracker"          => ThreatType.Tracker,
                "Malware"          => ThreatType.Malware,
                "Phishing"         => ThreatType.Phishing,
                "DataExfiltration" => ThreatType.DataExfiltration,
                "Miner"            => ThreatType.Miner,
                "Fingerprinting"   => ThreatType.Fingerprinting,
                "MitM"             => ThreatType.MitM,
                _                  => ThreatType.Other
            };

            return verdict switch
            {
                "BLOCK" => new AIVerdict { Verdict = VerdictType.Block, Confidence = confidence, Reason = reason, ThreatType = threatType, Source = source },
                "WARN"  => new AIVerdict { Verdict = VerdictType.Warn,  Confidence = confidence, Reason = reason, ThreatType = threatType, Source = source },
                _       => new AIVerdict { Verdict = VerdictType.Safe,  Confidence = confidence, Reason = reason, Source = source }
            };
        }
        catch
        {
            return AIVerdict.Fallback("No se pudo parsear la respuesta de la IA");
        }
    }
}
