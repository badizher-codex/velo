using System.Net;

namespace VELO.DNS.Providers;

public interface IDoHProvider
{
    string Name { get; }
    Task<DnsResult> ResolveAsync(string hostname, CancellationToken ct = default);
}

public record DnsResult(IPAddress[] Addresses, TimeSpan TTL);
