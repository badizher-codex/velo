using VELO.Core.Council;
using Xunit;

namespace VELO.Core.Tests.Council;

/// <summary>
/// Phase 4.1 chunk A — coverage for the Council Mode DTOs. These types have
/// no I/O, no side effects, no async surface — pure data with validated
/// constructors. Tests pin invariants that the orchestrator and UI rely on.
/// </summary>
public class CouncilModelsTests
{
    // ── CouncilProviderMap ──────────────────────────────────────────────

    [Theory]
    [InlineData(CouncilProvider.Claude,  "council-claude")]
    [InlineData(CouncilProvider.ChatGpt, "council-chatgpt")]
    [InlineData(CouncilProvider.Grok,    "council-grok")]
    [InlineData(CouncilProvider.Local,   "council-ollama")]
    public void ProviderMap_ToContainerId_returnsCanonicalId(CouncilProvider p, string expected)
    {
        Assert.Equal(expected, CouncilProviderMap.ToContainerId(p));
    }

    [Theory]
    [InlineData("council-claude",  CouncilProvider.Claude)]
    [InlineData("council-chatgpt", CouncilProvider.ChatGpt)]
    [InlineData("council-grok",    CouncilProvider.Grok)]
    [InlineData("council-ollama",  CouncilProvider.Local)]
    public void ProviderMap_FromContainerId_returnsCanonicalProvider(string id, CouncilProvider expected)
    {
        Assert.Equal(expected, CouncilProviderMap.FromContainerId(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("personal")]
    [InlineData("banking")]
    [InlineData("council-unknown")]
    public void ProviderMap_FromContainerId_returnsNullForNonCouncil(string? id)
    {
        Assert.Null(CouncilProviderMap.FromContainerId(id));
    }

    [Fact]
    public void ProviderMap_All_isFourCanonicalProvidersInOrder()
    {
        Assert.Equal(4, CouncilProviderMap.All.Count);
        Assert.Equal(CouncilProvider.Claude,  CouncilProviderMap.All[0]);
        Assert.Equal(CouncilProvider.ChatGpt, CouncilProviderMap.All[1]);
        Assert.Equal(CouncilProvider.Grok,    CouncilProviderMap.All[2]);
        Assert.Equal(CouncilProvider.Local,   CouncilProviderMap.All[3]);
    }

    [Fact]
    public void ProviderMap_DefaultHomeUrl_returnsAboutBlankForLocal()
    {
        // Local panel doesn't host a webview — moderator runs in-process via ChatDelegate.
        Assert.Equal("about:blank", CouncilProviderMap.DefaultHomeUrl(CouncilProvider.Local));
    }

    [Fact]
    public void ProviderMap_DefaultHomeUrl_returnsHttpsForCloudProviders()
    {
        Assert.StartsWith("https://", CouncilProviderMap.DefaultHomeUrl(CouncilProvider.Claude));
        Assert.StartsWith("https://", CouncilProviderMap.DefaultHomeUrl(CouncilProvider.ChatGpt));
        Assert.StartsWith("https://", CouncilProviderMap.DefaultHomeUrl(CouncilProvider.Grok));
    }

    [Fact]
    public void ProviderMap_EnabledSettingKey_matchesAppSettingsConstants()
    {
        // Pin the wire-up: if the constants in AppSettings.cs drift, the orchestrator
        // would silently read the wrong key and the disclaimer flow would break.
        Assert.Equal("council.enabled.claude",  CouncilProviderMap.EnabledSettingKey(CouncilProvider.Claude));
        Assert.Equal("council.enabled.chatgpt", CouncilProviderMap.EnabledSettingKey(CouncilProvider.ChatGpt));
        Assert.Equal("council.enabled.grok",    CouncilProviderMap.EnabledSettingKey(CouncilProvider.Grok));
        Assert.Equal("council.enabled.ollama",  CouncilProviderMap.EnabledSettingKey(CouncilProvider.Local));
    }

    // ── CouncilCapture ──────────────────────────────────────────────────

    [Fact]
    public void Capture_Create_generatesGuidIdAndStampsUtcNow()
    {
        var before = DateTime.UtcNow;
        var capture = CouncilCapture.Create(
            CouncilProvider.Claude, CouncilCaptureType.Text, "hola", "https://claude.ai/x");
        var after = DateTime.UtcNow;

        Assert.False(string.IsNullOrEmpty(capture.Id));
        Assert.Equal(32, capture.Id.Length); // GUID "N" format.
        Assert.InRange(capture.CapturedAtUtc, before, after);
    }

    [Fact]
    public void Capture_Create_rejectsNullContent()
    {
        Assert.Throws<ArgumentNullException>(() => CouncilCapture.Create(
            CouncilProvider.Claude, CouncilCaptureType.Text, null!, "https://claude.ai"));
    }

    [Fact]
    public void Capture_Create_allowsEmptySourceUrl()
    {
        // Local panel has no webview, so captures from there have no source URL.
        var capture = CouncilCapture.Create(
            CouncilProvider.Local, CouncilCaptureType.Text, "local reply", "");
        Assert.Equal("", capture.SourceUrl);
    }

    [Fact]
    public void Capture_Create_normalizesNullSourceUrlToEmptyString()
    {
        var capture = CouncilCapture.Create(
            CouncilProvider.Claude, CouncilCaptureType.Text, "x", null!);
        Assert.Equal("", capture.SourceUrl);
    }

    // ── CouncilMessage ──────────────────────────────────────────────────

    [Fact]
    public void Message_UserPrompt_setsRoleAndNullProvider()
    {
        var msg = CouncilMessage.UserPrompt("master prompt");

        Assert.Equal(CouncilMessageRole.User, msg.Role);
        Assert.Null(msg.SourceProvider);
        Assert.Equal("master prompt", msg.Text);
        Assert.Empty(msg.CapturedRefs);
    }

    [Fact]
    public void Message_PanelReply_carriesProviderAndCapturedRefs()
    {
        var refs = new[] { "cap1", "cap2" };
        var msg = CouncilMessage.PanelReply(CouncilProvider.Claude, "reply", refs);

        Assert.Equal(CouncilMessageRole.Panel, msg.Role);
        Assert.Equal(CouncilProvider.Claude, msg.SourceProvider);
        Assert.Equal(2, msg.CapturedRefs.Count);
        Assert.Equal("cap1", msg.CapturedRefs[0]);
    }

    [Fact]
    public void Message_Synthesis_setsModeratorRoleNoProvider()
    {
        var msg = CouncilMessage.Synthesis("synthesised");
        Assert.Equal(CouncilMessageRole.Moderator, msg.Role);
        Assert.Null(msg.SourceProvider);
    }

    [Fact]
    public void Message_System_setsSystemRoleNoProvider()
    {
        var msg = CouncilMessage.System("Panel Grok no disponible");
        Assert.Equal(CouncilMessageRole.System, msg.Role);
        Assert.Null(msg.SourceProvider);
    }

    // ── CouncilPanel ────────────────────────────────────────────────────

    [Fact]
    public void Panel_NewInstance_setsContainerIdAndHomeUrl()
    {
        var panel = new CouncilPanel(CouncilProvider.ChatGpt);

        Assert.Equal(CouncilProvider.ChatGpt, panel.Provider);
        Assert.Equal("council-chatgpt", panel.ContainerId);
        Assert.Equal("https://chat.openai.com/", panel.CurrentUrl);
        Assert.False(panel.IsAvailable);
        Assert.Empty(panel.Captures);
    }

    [Fact]
    public void Panel_AddCapture_rejectsMismatchedProvider()
    {
        var panel = new CouncilPanel(CouncilProvider.Claude);
        var foreign = CouncilCapture.Create(
            CouncilProvider.Grok, CouncilCaptureType.Text, "x", "https://grok.com");

        Assert.Throws<ArgumentException>(() => panel.AddCapture(foreign));
    }

    [Fact]
    public void Panel_RemoveCapture_returnsTrueWhenFound()
    {
        var panel = new CouncilPanel(CouncilProvider.Claude);
        var cap = panel.AddCapture(CouncilCapture.Create(
            CouncilProvider.Claude, CouncilCaptureType.Text, "x", "https://claude.ai"));

        Assert.True(panel.RemoveCapture(cap.Id));
        Assert.Empty(panel.Captures);
    }

    [Fact]
    public void Panel_RemoveCapture_returnsFalseWhenIdMissing()
    {
        var panel = new CouncilPanel(CouncilProvider.Claude);
        Assert.False(panel.RemoveCapture("missing-id"));
    }

    // ── CouncilSession ──────────────────────────────────────────────────

    [Fact]
    public void Session_DefaultConstructor_seedsFourDisabledPanelsInCanonicalOrder()
    {
        var session = new CouncilSession();

        Assert.Equal(4, session.Panels.Count);
        Assert.Equal(CouncilProvider.Claude,  session.Panels[0].Provider);
        Assert.Equal(CouncilProvider.ChatGpt, session.Panels[1].Provider);
        Assert.Equal(CouncilProvider.Grok,    session.Panels[2].Provider);
        Assert.Equal(CouncilProvider.Local,   session.Panels[3].Provider);
        Assert.All(session.Panels, p => Assert.False(p.IsAvailable));
        Assert.Equal(0, session.AvailablePanelCount);
        Assert.Empty(session.Transcript);
    }

    [Fact]
    public void Session_GetPanel_returnsByProvider()
    {
        var session = new CouncilSession();
        var panel = session.GetPanel(CouncilProvider.Grok);
        Assert.Equal(CouncilProvider.Grok, panel.Provider);
    }

    [Fact]
    public void Session_Constructor_rejectsWrongPanelCount()
    {
        var threePanels = new[]
        {
            new CouncilPanel(CouncilProvider.Claude),
            new CouncilPanel(CouncilProvider.ChatGpt),
            new CouncilPanel(CouncilProvider.Grok),
        };
        Assert.Throws<ArgumentException>(() => new CouncilSession(threePanels));
    }

    [Fact]
    public void Session_Constructor_rejectsWrongPanelOrder()
    {
        var outOfOrder = new[]
        {
            new CouncilPanel(CouncilProvider.ChatGpt), // expected Claude
            new CouncilPanel(CouncilProvider.Claude),
            new CouncilPanel(CouncilProvider.Grok),
            new CouncilPanel(CouncilProvider.Local),
        };
        Assert.Throws<ArgumentException>(() => new CouncilSession(outOfOrder));
    }

    [Fact]
    public void Session_HasStableIdAndStartedAtUtc()
    {
        var before = DateTime.UtcNow;
        var session = new CouncilSession();
        var after = DateTime.UtcNow;

        Assert.False(string.IsNullOrEmpty(session.Id));
        Assert.Equal(32, session.Id.Length);
        Assert.InRange(session.StartedAtUtc, before, after);
    }
}
