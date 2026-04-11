using System.Net;
using System.Text.Json;

namespace VELO.DNS.Providers;

public class Quad9Provider(HttpClient http) : IDoHProvider
{
    private readonly HttpClient _http = http;
    private const string URL = "https://dns.quad9.net/dns-query";

    public string Name => "Quad9";

    public async Task<DnsResult> ResolveAsync(string hostname, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{URL}?name={Uri.EscapeDataString(hostname)}&type=A");
        request.Headers.Add("Accept", "application/dns-json");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseResponse(json);
    }

    private static DnsResult ParseResponse(string json) => DoHJsonParser.Parse(json);
}
