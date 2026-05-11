using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

public class DomainAgeProbeTests
{
    // ── GetEtldPlusOne ───────────────────────────────────────────────────

    [Theory]
    [InlineData("example.com",           "example.com")]
    [InlineData("www.example.com",       "example.com")]
    [InlineData("sub.example.com",       "example.com")]
    [InlineData("a.b.c.example.com",     "example.com")]
    [InlineData("EXAMPLE.COM",           "example.com")]
    [InlineData("paypal-secure.xyz",     "paypal-secure.xyz")]
    public void GetEtldPlusOne_ReturnsExpected(string host, string expected)
    {
        Assert.Equal(expected, DomainAgeProbe.GetEtldPlusOne(host));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]               // single label
    [InlineData("192.168.1.1")]             // IPv4
    [InlineData("203.0.113.5")]             // IPv4
    [InlineData("[::1]")]                   // IPv6 literal
    [InlineData("2001:db8::1")]             // IPv6
    public void GetEtldPlusOne_RejectsInvalid(string host)
    {
        Assert.Equal("", DomainAgeProbe.GetEtldPlusOne(host));
    }

    // ── ParseBootstrap ───────────────────────────────────────────────────

    [Fact]
    public void ParseBootstrap_ExtractsTldToServerMap()
    {
        var json = """
        {
          "version": "1.0",
          "publication": "2025-01-01T00:00:00Z",
          "services": [
            [
              ["xyz", "top"],
              ["https://rdap.centralnic.com/"]
            ],
            [
              ["com", "net"],
              ["https://rdap.verisign.com/com/v1/", "https://other.example/"]
            ]
          ]
        }
        """;

        var map = DomainAgeProbe.ParseBootstrap(json);

        Assert.Equal(4, map.Count);
        Assert.Equal("https://rdap.centralnic.com/", map["xyz"]);
        Assert.Equal("https://rdap.centralnic.com/", map["top"]);
        Assert.Equal("https://rdap.verisign.com/com/v1/", map["com"]); // first URL wins
        Assert.Equal("https://rdap.verisign.com/com/v1/", map["net"]);
    }

    [Fact]
    public void ParseBootstrap_AddsTrailingSlash()
    {
        var json = """
        { "services": [ [ ["xyz"], ["https://rdap.example.com"] ] ] }
        """;

        var map = DomainAgeProbe.ParseBootstrap(json);
        Assert.Equal("https://rdap.example.com/", map["xyz"]);
    }

    [Fact]
    public void ParseBootstrap_TolerantOfMalformedEntries()
    {
        // Mix of valid + malformed entries — keeps valid, drops broken.
        var json = """
        {
          "services": [
            [ ["xyz"], ["https://good.example/"] ],
            "not-an-array",
            [ ["only-one-array"] ],
            [ ["empty-server"], [] ],
            [ ["net"], ["https://net.example/"] ]
          ]
        }
        """;

        var map = DomainAgeProbe.ParseBootstrap(json);
        Assert.Equal("https://good.example/", map["xyz"]);
        Assert.Equal("https://net.example/",  map["net"]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void ParseBootstrap_EmptyOrInvalid_ReturnsEmptyMap()
    {
        Assert.Empty(DomainAgeProbe.ParseBootstrap(""));
        Assert.Empty(DomainAgeProbe.ParseBootstrap("not json"));
        Assert.Empty(DomainAgeProbe.ParseBootstrap("{}"));
        Assert.Empty(DomainAgeProbe.ParseBootstrap("""{"services":"wrong-type"}"""));
    }

    // ── ParseRegistrationDate ────────────────────────────────────────────

    [Fact]
    public void ParseRegistrationDate_FindsRegistrationEvent()
    {
        var json = """
        {
          "events": [
            { "eventAction": "last changed", "eventDate": "2025-06-01T00:00:00Z" },
            { "eventAction": "registration", "eventDate": "2020-01-15T12:30:00Z" },
            { "eventAction": "expiration",   "eventDate": "2026-01-15T00:00:00Z" }
          ]
        }
        """;

        var date = DomainAgeProbe.ParseRegistrationDate(json);
        Assert.NotNull(date);
        Assert.Equal(new DateTime(2020, 1, 15, 12, 30, 0, DateTimeKind.Utc), date!.Value);
    }

    [Fact]
    public void ParseRegistrationDate_CaseInsensitiveAction()
    {
        var json = """
        { "events": [ { "eventAction": "Registration", "eventDate": "2024-03-01T00:00:00Z" } ] }
        """;

        var date = DomainAgeProbe.ParseRegistrationDate(json);
        Assert.NotNull(date);
    }

    [Fact]
    public void ParseRegistrationDate_MissingEvents_ReturnsNull()
    {
        Assert.Null(DomainAgeProbe.ParseRegistrationDate("{}"));
        Assert.Null(DomainAgeProbe.ParseRegistrationDate("""{"events":[]}"""));
        Assert.Null(DomainAgeProbe.ParseRegistrationDate("""{"events":[{"eventAction":"transfer","eventDate":"2024-01-01T00:00:00Z"}]}"""));
    }

    [Fact]
    public void ParseRegistrationDate_MalformedJson_ReturnsNull()
    {
        Assert.Null(DomainAgeProbe.ParseRegistrationDate(""));
        Assert.Null(DomainAgeProbe.ParseRegistrationDate("not json"));
        Assert.Null(DomainAgeProbe.ParseRegistrationDate("""{"events":[{"eventAction":"registration","eventDate":"not a date"}]}"""));
    }

    // ── GetDomainAgeDaysAsync (with stubbed HttpGet) ─────────────────────

    private static DomainAgeProbe NewProbe(Dictionary<string, string> responses)
    {
        var p = new DomainAgeProbe { Enabled = true };
        p.HttpGet = (url, _) =>
            Task.FromResult(responses.TryGetValue(url, out var body) ? body : null);
        return p;
    }

    private const string Bootstrap = """
        {
          "services": [
            [ ["xyz", "top"], ["https://rdap.example/"] ],
            [ ["com"],        ["https://com-rdap.example/"] ]
          ]
        }
        """;

    private static string RegisteredDaysAgo(int days)
    {
        var d = DateTime.UtcNow.AddDays(-days).ToString("o");
        return $$"""
        { "events": [ { "eventAction": "registration", "eventDate": "{{d}}" } ] }
        """;
    }

    [Fact]
    public async Task GetDomainAge_DisabledByDefault_ReturnsZero()
    {
        var p = new DomainAgeProbe(); // Enabled = false (default)
        p.HttpGet = (_, _) => Task.FromResult<string?>("should not be hit");
        Assert.Equal(0, await p.GetDomainAgeDaysAsync("example.com"));
    }

    [Fact]
    public async Task GetDomainAge_EmptyHost_ReturnsZero()
    {
        var p = new DomainAgeProbe { Enabled = true };
        Assert.Equal(0, await p.GetDomainAgeDaysAsync(""));
        Assert.Equal(0, await p.GetDomainAgeDaysAsync("   "));
    }

    [Fact]
    public async Task GetDomainAge_HappyPath_ReturnsDays()
    {
        var p = NewProbe(new()
        {
            ["https://data.iana.org/rdap/dns.json"]      = Bootstrap,
            ["https://rdap.example/domain/example.xyz"]  = RegisteredDaysAgo(7),
        });

        var days = await p.GetDomainAgeDaysAsync("example.xyz");
        Assert.InRange(days, 6, 8); // tolerate clock drift
    }

    [Fact]
    public async Task GetDomainAge_CachesResult_NoSecondLookup()
    {
        int hits = 0;
        var p = new DomainAgeProbe { Enabled = true };
        p.HttpGet = (url, _) =>
        {
            hits++;
            if (url.EndsWith("dns.json")) return Task.FromResult<string?>(Bootstrap);
            if (url.Contains("example.xyz")) return Task.FromResult<string?>(RegisteredDaysAgo(5));
            return Task.FromResult<string?>(null);
        };

        await p.GetDomainAgeDaysAsync("example.xyz");
        await p.GetDomainAgeDaysAsync("example.xyz");
        await p.GetDomainAgeDaysAsync("sub.example.xyz"); // same eTLD+1

        Assert.Equal(2, hits); // 1 bootstrap + 1 result; subsequent lookups hit cache
    }

    [Fact]
    public async Task GetDomainAge_UnknownTld_ReturnsZero()
    {
        var p = NewProbe(new()
        {
            ["https://data.iana.org/rdap/dns.json"] = Bootstrap,
            // no entry for .unknown
        });

        Assert.Equal(0, await p.GetDomainAgeDaysAsync("evil.unknown"));
    }

    [Fact]
    public async Task GetDomainAge_NetworkFailure_ReturnsZero()
    {
        var p = new DomainAgeProbe { Enabled = true };
        p.HttpGet = (_, _) => Task.FromResult<string?>(null); // all requests fail

        Assert.Equal(0, await p.GetDomainAgeDaysAsync("example.com"));
    }

    [Fact]
    public async Task GetDomainAge_MalformedRdapResponse_ReturnsZero()
    {
        var p = NewProbe(new()
        {
            ["https://data.iana.org/rdap/dns.json"]     = Bootstrap,
            ["https://rdap.example/domain/example.xyz"] = "not json at all",
        });

        Assert.Equal(0, await p.GetDomainAgeDaysAsync("example.xyz"));
    }

    [Fact]
    public async Task GetDomainAge_FreshDomain_ReturnsLowDays()
    {
        var p = NewProbe(new()
        {
            ["https://data.iana.org/rdap/dns.json"]    = Bootstrap,
            ["https://rdap.example/domain/freshie.xyz"] = RegisteredDaysAgo(3),
        });

        var days = await p.GetDomainAgeDaysAsync("freshie.xyz");
        Assert.InRange(days, 2, 4);
    }

    [Fact]
    public async Task GetDomainAge_BootstrapTtlElapsed_ReloadsBootstrap()
    {
        int bootstrapHits = 0;
        var p = new DomainAgeProbe
        {
            Enabled      = true,
            BootstrapTtl = TimeSpan.Zero, // every lookup re-fetches bootstrap
        };
        p.HttpGet = (url, _) =>
        {
            if (url.EndsWith("dns.json")) { bootstrapHits++; return Task.FromResult<string?>(Bootstrap); }
            if (url.Contains("a.xyz"))    return Task.FromResult<string?>(RegisteredDaysAgo(1));
            if (url.Contains("b.xyz"))    return Task.FromResult<string?>(RegisteredDaysAgo(2));
            return Task.FromResult<string?>(null);
        };

        await p.GetDomainAgeDaysAsync("a.xyz");
        await p.GetDomainAgeDaysAsync("b.xyz");
        Assert.Equal(2, bootstrapHits); // each lookup re-loaded bootstrap
    }
}
