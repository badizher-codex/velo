using VELO.Security.AI.Models;
using VELO.Security.Models;

namespace VELO.Security;

/// <summary>
/// Generates human-readable, three-part security explanations for any threat verdict.
/// All output is in the user's language (es/en). Defaults to Spanish.
/// </summary>
public class ExplanationGenerator
{
    private const string BaseLearnMoreUrl = "velo://docs/threats/";

    public SecurityExplanation Generate(SecurityVerdict verdict, string language = "es")
    {
        var type = verdict.ThreatType;
        if (!ExplanationTemplates.ByThreatType.TryGetValue(type, out var t))
            t = ExplanationTemplates.ByThreatType[ThreatType.Other];

        return Build(t, language, new Dictionary<string, string>
        {
            ["source"]        = verdict.Source ?? type.ToString(),
            ["blocklistName"] = verdict.Source ?? "VELO Blocklist",
            ["confidence"]    = verdict.Confidence.ToString(),
            ["bigTechName"]   = InferBigTech(verdict.Source),
            ["tlsError"]      = "",
            ["score"]         = "",
            ["patterns"]      = "",
        });
    }

    public SecurityExplanation GenerateFromBlocklist(ThreatType type, string domain,
        string blocklistName, int confidence = 100, string language = "es")
    {
        if (!ExplanationTemplates.ByThreatType.TryGetValue(type, out var t))
            t = ExplanationTemplates.ByThreatType[ThreatType.Tracker];

        return Build(t, language, new Dictionary<string, string>
        {
            ["source"]        = domain,
            ["blocklistName"] = blocklistName,
            ["confidence"]    = ((int)(confidence * 100)).ToString(),
            ["bigTechName"]   = InferBigTech(domain),
            ["tlsError"]      = "",
            ["score"]         = "",
            ["patterns"]      = "",
        });
    }

    public SecurityExplanation GenerateFromAI(string aiReason, ThreatType type,
        int confidence = 0, string language = "es")
    {
        if (!ExplanationTemplates.ByThreatType.TryGetValue(type, out var t))
            t = ExplanationTemplates.ByThreatType[ThreatType.Other];

        return Build(t, language, new Dictionary<string, string>
        {
            ["source"]        = "análisis de IA",
            ["blocklistName"] = "Análisis IA",
            ["confidence"]    = confidence.ToString(),
            ["bigTechName"]   = "",
            ["tlsError"]      = aiReason,
            ["score"]         = "",
            ["patterns"]      = aiReason,
        });
    }

    public SecurityExplanation GenerateFromTLS(string tlsErrorKey, string language = "es")
    {
        if (!ExplanationTemplates.ByTlsError.TryGetValue(tlsErrorKey, out var t))
            t = ExplanationTemplates.ByTlsError["self-signed"];

        return Build(t, language, new Dictionary<string, string>
        {
            ["source"]        = "TLS",
            ["blocklistName"] = "",
            ["confidence"]    = "100",
            ["bigTechName"]   = "",
            ["tlsError"]      = tlsErrorKey,
            ["score"]         = "",
            ["patterns"]      = "",
        });
    }

    public SecurityExplanation GenerateFromScript(int riskScore, string patternsDescription,
        string language = "es")
    {
        var t = ExplanationTemplates.GetScriptTemplate(riskScore);

        return Build(t, language, new Dictionary<string, string>
        {
            ["source"]        = "script",
            ["blocklistName"] = "",
            ["confidence"]    = "100",
            ["bigTechName"]   = "",
            ["tlsError"]      = "",
            ["score"]         = riskScore.ToString(),
            ["patterns"]      = patternsDescription,
        });
    }

    private static SecurityExplanation Build(ExplanationTemplates.Template t,
        string language, Dictionary<string, string> vars)
    {
        bool isEs = !language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        var what  = isEs ? t.WhatHappened_es : t.WhatHappened_en;
        var why   = isEs ? t.WhyBlocked_es   : t.WhyBlocked_en;
        var means = isEs ? t.WhatItMeans_es  : t.WhatItMeans_en;

        foreach (var (key, value) in vars)
        {
            what  = what.Replace($"{{{key}}}", value);
            why   = why.Replace($"{{{key}}}", value);
            means = means.Replace($"{{{key}}}", value);
        }

        return new SecurityExplanation(what, why, means,
            LearnMoreUrl: BaseLearnMoreUrl + t.LearnMoreSlug);
    }

    private static string InferBigTech(string? source)
    {
        if (source is null) return "el rastreador";
        var s = source.ToLowerInvariant();
        if (s.Contains("google") || s.Contains("doubleclick") || s.Contains("gstatic") || s.Contains("googleapis"))
            return "Google";
        if (s.Contains("facebook") || s.Contains("meta") || s.Contains("instagram"))
            return "Meta";
        if (s.Contains("amazon") || s.Contains("aws"))
            return "Amazon";
        if (s.Contains("microsoft") || s.Contains("bing"))
            return "Microsoft";
        if (s.Contains("twitter") || s.Contains("twimg"))
            return "X/Twitter";
        if (s.Contains("tiktok") || s.Contains("bytedance"))
            return "TikTok";
        return "el rastreador";
    }
}
