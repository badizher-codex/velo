using System.Security.Cryptography;
using System.Text;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Security.AI.Models;

namespace VELO.Security.AI;

public class SecurityCache(SecurityCacheRepository repo)
{
    private readonly SecurityCacheRepository _repo = repo;

    private static readonly Dictionary<VerdictType, TimeSpan> TTL = new()
    {
        { VerdictType.Safe,  TimeSpan.FromHours(24) },
        { VerdictType.Warn,  TimeSpan.FromHours(1)  },
        { VerdictType.Block, TimeSpan.MaxValue       }
    };

    public async Task<AIVerdict?> GetAsync(ThreatContext context)
    {
        var key = ComputeKey(context);
        var cached = await _repo.GetByKeyAsync(key);
        if (cached == null) return null;

        if (DateTime.UtcNow > cached.ExpiresAt)
        {
            await _repo.DeleteAsync(key);
            return null;
        }

        return new AIVerdict
        {
            Verdict    = Enum.Parse<VerdictType>(cached.Verdict),
            Confidence = cached.Confidence,
            Reason     = cached.Reason ?? "",
            ThreatType = cached.ThreatType != null ? Enum.Parse<ThreatType>(cached.ThreatType) : ThreatType.None,
            Source     = cached.Source ?? "CACHE"
        };
    }

    public async Task SetAsync(ThreatContext context, AIVerdict verdict)
    {
        var key = ComputeKey(context);
        var ttl = TTL[verdict.Verdict];

        await _repo.SaveAsync(new CachedVerdict
        {
            CacheKey   = key,
            Domain     = context.Domain,
            Verdict    = verdict.Verdict.ToString(),
            Confidence = verdict.Confidence,
            Reason     = verdict.Reason,
            ThreatType = verdict.ThreatType.ToString(),
            Source     = verdict.Source,
            CachedAt   = DateTime.UtcNow,
            ExpiresAt  = ttl == TimeSpan.MaxValue
                            ? DateTime.MaxValue
                            : DateTime.UtcNow.Add(ttl)
        });
    }

    private static string ComputeKey(ThreatContext ctx)
    {
        var raw = $"{ctx.Domain}|{ctx.ResourceType}|{ctx.ScriptHash}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
