using System.Net;
using System.Net.Http;
using System.Text;
using VELO.Vault.Security;
using Xunit;

namespace VELO.Vault.Tests;

public class HibpClientTests
{
    /// <summary>Stub handler that records the URL and returns a canned body.</summary>
    private sealed class StubHandler(string body, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    // ── Spec § 5.7 #5 ───────────────────────────────────────────────────

    [Fact]
    public async Task HibpClient_OnlySendsFirst5CharsOfHash()
    {
        // Real HIBP API contract: GET /range/{first5HexCharsOfSHA1Upper}.
        // The full hash, password and any longer prefix MUST NOT be sent.
        var handler = new StubHandler("DEADBEEF:1\r\n");
        var client  = new HibpClient(new HttpClient(handler));

        await client.GetBreachCountAsync("hunter2");

        Assert.Single(handler.RequestedUrls);
        var url = handler.RequestedUrls[0];

        // SHA1("hunter2") = F3BBBD66A63D4BF1747940578EC3D0103530E21D
        // First 5 hex chars (uppercase): F3BBB
        Assert.EndsWith("/range/F3BBB", url);

        // The remainder of the hash, the password itself, and longer
        // prefixes MUST NOT appear anywhere in the request URL.
        Assert.DoesNotContain("F3BBBD",  url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", url, StringComparison.OrdinalIgnoreCase);
    }

    // ── Spec § 5.7 #6 ───────────────────────────────────────────────────

    [Fact]
    public void HibpClient_ParsesResponseCorrectly()
    {
        // Real-shaped response: line-separated SUFFIX:COUNT, mixed case,
        // possibly with stray whitespace and CRLF endings. Suffix that
        // matches our hash should yield its count.
        var body = """
            BD66A63D4BF1747940578EC3D0103530E21D:142
            ABCDEF1234567890ABCDEF1234567890ABCDE:99999
            0000000000000000000000000000000000000:1
            """;

        var count = HibpClient.ParseRangeResponse(body, "BD66A63D4BF1747940578EC3D0103530E21D");
        Assert.Equal(142, count);

        // Case-insensitive match — HIBP returns uppercase but be defensive.
        var lower = HibpClient.ParseRangeResponse(body, "bd66a63d4bf1747940578ec3d0103530e21d");
        Assert.Equal(142, lower);
    }

    // ── Spec § 5.7 #7 ───────────────────────────────────────────────────

    [Fact]
    public void HibpClient_ReturnsZero_WhenPasswordNotInResponse()
    {
        var body = """
            DEADBEEF1234567890DEADBEEF1234567890D:1
            ABCDEF1234567890ABCDEF1234567890ABCDE:42
            """;

        var count = HibpClient.ParseRangeResponse(body, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        Assert.Equal(0, count);
    }

    // ── Bonus: full round-trip with a stubbed network ───────────────────

    [Fact]
    public async Task GetBreachCount_RoundTrips_AgainstStubbedRange()
    {
        // SHA1 of "hunter2" = F3BBBD66A63D4BF1747940578EC3D0103530E21D
        // → prefix "F3BBB", suffix "D66A63D4BF1747940578EC3D0103530E21D".
        var body = """
            D66A63D4BF1747940578EC3D0103530E21D:65
            FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:1
            """;
        var handler = new StubHandler(body);
        var client  = new HibpClient(new HttpClient(handler));

        var count = await client.GetBreachCountAsync("hunter2");
        Assert.Equal(65, count);
    }

    [Fact]
    public void Sha1PrefixSuffix_SplitsAtFifthChar()
    {
        var (prefix, suffix) = HibpClient.Sha1PrefixSuffix("password");
        // SHA1("password") = 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8.
        Assert.Equal("5BAA6", prefix);
        Assert.Equal("1E4C9B93F3F0682250B6CF8331B7EE68FD8", suffix);
        Assert.Equal(5,  prefix.Length);
        Assert.Equal(35, suffix.Length);
    }
}
