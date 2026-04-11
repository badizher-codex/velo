using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using VELO.DNS.Providers;

namespace VELO.DNS;

public class DoHResolver(IDoHProvider provider, ILogger<DoHResolver> logger)
{
    private readonly IDoHProvider _provider = provider;
    private readonly ILogger<DoHResolver> _logger = logger;

    // Simple in-memory DNS cache: hostname → (addresses, expiry)
    private readonly ConcurrentDictionary<string, (IPAddress[] Addresses, DateTime Expiry)> _cache = new();

    public async Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken ct = default)
    {
        hostname = hostname.ToLowerInvariant();

        // 1. Cache hit
        if (_cache.TryGetValue(hostname, out var cached) && DateTime.UtcNow < cached.Expiry)
            return cached.Addresses;

        // 2. Resolve via DoH
        try
        {
            var result = await _provider.ResolveAsync(hostname, ct);

            // 3. DNS rebinding check: public domain resolving to private IP
            foreach (var ip in result.Addresses)
            {
                if (IsPublicDomain(hostname) && IsPrivateIp(ip))
                {
                    _logger.LogWarning("DNS rebinding attempt: {Hostname} → {IP}", hostname, ip);
                    throw new DnsRebindingException(hostname, ip);
                }
            }

            _cache[hostname] = (result.Addresses, DateTime.UtcNow.Add(result.TTL));
            return result.Addresses;
        }
        catch (DnsRebindingException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DoH resolution failed for {Hostname}, falling back to system DNS", hostname);
            // Fallback: system DNS
            var systemResult = await Dns.GetHostAddressesAsync(hostname, ct);
            return systemResult;
        }
    }

    public void ClearCache() => _cache.Clear();

    private static bool IsPublicDomain(string hostname)
        => !hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        && !hostname.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        && !IPAddress.TryParse(hostname, out _);

    private static bool IsPrivateIp(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || b[0] == 127;
    }
}

public class DnsRebindingException(string hostname, IPAddress resolvedIp)
    : Exception($"DNS rebinding detected: {hostname} resolved to private IP {resolvedIp}")
{
    public string Hostname { get; } = hostname;
    public IPAddress ResolvedIp { get; } = resolvedIp;
}
