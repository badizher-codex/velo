using Microsoft.Extensions.Logging.Abstractions;
using VELO.Core.Search;
using VELO.Data;
using VELO.Data.Repositories;
using Xunit;

namespace VELO.Core.Tests;

// v2.4.64 — The omnibox recognised only http/https/velo and glued "https://" onto
// anything else, so `file:///C:/x.html` became `https://file:///C:/x.html` and VELO
// could not open a local file at all. Found while trying to run a local diagnostic
// page inside VELO.
public class SearchEngineServiceTests
{
    private static SearchEngineService Build()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        db.InitializeAsync().GetAwaiter().GetResult();
        return new SearchEngineService(new SettingsRepository(db));
    }

    [Theory]
    [InlineData("file:///C:/Users/x/page.html")]
    [InlineData("http://127.0.0.1:8765/page.html")]
    [InlineData("https://example.com")]
    [InlineData("velo://newtab")]
    [InlineData("about:blank")]
    [InlineData("view-source:https://example.com")]
    public async Task ResolveInput_LeavesKnownSchemesUntouched(string input)
        => Assert.Equal(input, await Build().ResolveInputAsync(input));

    [Fact]
    public async Task ResolveInput_IsCaseInsensitiveOnTheScheme()
        => Assert.Equal("FILE:///C:/x.html", await Build().ResolveInputAsync("FILE:///C:/x.html"));

    [Fact]
    public async Task ResolveInput_StillAddsHttpsToABareDomain()
        => Assert.Equal("https://example.com", await Build().ResolveInputAsync("example.com"));

    [Fact]
    public async Task ResolveInput_StillSearchesForPlainText()
    {
        var result = await Build().ResolveInputAsync("cómo se hace el pan");
        Assert.StartsWith("https://", result);
        Assert.Contains("q=", result);
    }

    // Typing these in an address bar is a self-XSS / phishing vector; Chromium
    // blocks top-level navigation to them, and so do we — they stay searches.
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void IsSearchQuery_DoesNotWhitelistScriptableSchemes(string input)
        => Assert.True(SearchEngineService.IsSearchQuery(input));

    [Theory]
    [InlineData("file:///C:/x.html", false)]
    [InlineData("edge://components", false)]
    [InlineData("example.com", false)]
    [InlineData("hola mundo", true)]
    public void IsSearchQuery_ClassifiesInput(string input, bool expected)
        => Assert.Equal(expected, SearchEngineService.IsSearchQuery(input));
}
