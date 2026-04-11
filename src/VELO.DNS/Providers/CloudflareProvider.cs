using System.Net;
using System.Text.Json;

namespace VELO.DNS.Providers;

public class CloudflareProvider(HttpClient http) : IDoHProvider
{
    private readonly HttpClient _http = http;
    private const string URL = "https://cloudflare-dns.com/dns-query";

    public string Name => "Cloudflare";

    public async Task<DnsResult> ResolveAsync(string hostname, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{URL}?name={Uri.EscapeDataString(hostname)}&type=A");
        request.Headers.Add("Accept", "application/dns-json");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return DoHJsonParser.Parse(json);
    }
}
