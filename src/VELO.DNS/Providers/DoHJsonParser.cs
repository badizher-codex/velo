using System.Net;
using System.Text.Json;

namespace VELO.DNS.Providers;

internal static class DoHJsonParser
{
    internal static DnsResult Parse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var addresses = new List<IPAddress>();
        int ttl = 300;

        if (doc.RootElement.TryGetProperty("Answer", out var answers))
        {
            foreach (var answer in answers.EnumerateArray())
            {
                if (answer.TryGetProperty("data", out var data) &&
                    IPAddress.TryParse(data.GetString(), out var ip))
                    addresses.Add(ip);

                if (answer.TryGetProperty("TTL", out var ttlProp))
                    ttl = ttlProp.GetInt32();
            }
        }

        if (addresses.Count == 0)
            throw new InvalidOperationException("DNS resolution returned no addresses");

        return new DnsResult(addresses.ToArray(), TimeSpan.FromSeconds(ttl));
    }
}
