using VELO.Core.Council;
using Xunit;

namespace VELO.Core.Tests.Council;

/// <summary>
/// Phase 4.1 chunk C — coverage for the WebMessage-to-typed-record parser.
/// Inputs mirror the actual JSON shapes <c>council-bridge.js</c> emits so
/// any drift on either side surfaces here.
/// </summary>
public class CouncilBridgeParserTests
{
    [Fact]
    public void Parse_returnsNullForEmptyOrWhitespace()
    {
        Assert.Null(CouncilBridgeParser.Parse("", CouncilProvider.Claude));
        Assert.Null(CouncilBridgeParser.Parse("   ", CouncilProvider.Claude));
    }

    [Fact]
    public void Parse_returnsNullForNonObjectJson()
    {
        Assert.Null(CouncilBridgeParser.Parse("[]", CouncilProvider.Claude));
        Assert.Null(CouncilBridgeParser.Parse("\"hello\"", CouncilProvider.Claude));
        Assert.Null(CouncilBridgeParser.Parse("42", CouncilProvider.Claude));
    }

    [Fact]
    public void Parse_returnsNullForMissingTypeField()
    {
        const string json = """{"foo":"bar"}""";
        Assert.Null(CouncilBridgeParser.Parse(json, CouncilProvider.Claude));
    }

    [Fact]
    public void Parse_returnsNullForNonCouncilTypePrefix()
    {
        // Other features (autofill, paste-guard, …) also use WebMessageReceived;
        // the Council parser must hand them back unparsed.
        const string json = """{"type":"autofill/detected","host":"example.com"}""";
        Assert.Null(CouncilBridgeParser.Parse(json, CouncilProvider.Claude));
    }

    [Fact]
    public void Parse_returnsNullForMalformedJson()
    {
        Assert.Null(CouncilBridgeParser.Parse("{not json", CouncilProvider.Claude));
    }

    // ── council/capture ──────────────────────────────────────────────────

    [Theory]
    [InlineData("text",     CouncilCaptureType.Text)]
    [InlineData("code",     CouncilCaptureType.Code)]
    [InlineData("table",    CouncilCaptureType.Table)]
    [InlineData("citation", CouncilCaptureType.Citation)]
    [InlineData("TEXT",     CouncilCaptureType.Text)]
    [InlineData("Code",     CouncilCaptureType.Code)]
    public void Parse_capture_decodesCaptureType(string raw, CouncilCaptureType expected)
    {
        var json = $$"""{"type":"council/capture","captureType":"{{raw}}","content":"hi","sourceUrl":"https://claude.ai/x"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.Claude);

        var cap = Assert.IsType<CouncilCaptureMessage>(msg);
        Assert.Equal(expected, cap.CaptureType);
        Assert.Equal("hi", cap.Content);
        Assert.Equal("https://claude.ai/x", cap.SourceUrl);
        Assert.Equal(CouncilProvider.Claude, cap.Provider);
    }

    [Fact]
    public void Parse_capture_returnsNullForUnknownCaptureType()
    {
        const string json = """{"type":"council/capture","captureType":"banana","content":"x"}""";
        Assert.Null(CouncilBridgeParser.Parse(json, CouncilProvider.Grok));
    }

    [Fact]
    public void Parse_capture_handlesMissingContentAsEmpty()
    {
        const string json = """{"type":"council/capture","captureType":"text"}""";
        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.ChatGpt);
        var cap = Assert.IsType<CouncilCaptureMessage>(msg);
        Assert.Equal("", cap.Content);
        Assert.Equal("", cap.SourceUrl);
    }

    // ── council/replyDetected ────────────────────────────────────────────

    [Fact]
    public void Parse_replyDetected_capturesTextAndSourceUrl()
    {
        const string json = """{"type":"council/replyDetected","text":"the reply","sourceUrl":"https://grok.com/x"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.Grok);

        var reply = Assert.IsType<CouncilReplyDetectedMessage>(msg);
        Assert.Equal("the reply", reply.Text);
        Assert.Equal("https://grok.com/x", reply.SourceUrl);
        Assert.Equal(CouncilProvider.Grok, reply.Provider);
    }

    // ── council/error ────────────────────────────────────────────────────

    [Fact]
    public void Parse_error_capturesMessageField()
    {
        const string json = """{"type":"council/error","message":"setAdapter parse failed"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.Local);

        var err = Assert.IsType<CouncilBridgeErrorMessage>(msg);
        Assert.Equal("setAdapter parse failed", err.ErrorText);
        Assert.Equal(CouncilProvider.Local, err.Provider);
    }

    [Fact]
    public void Parse_unknownCouncilSubtype_returnsNull()
    {
        // type starts with council/ but isn't a known subtype.
        const string json = """{"type":"council/futurething","data":"x"}""";
        Assert.Null(CouncilBridgeParser.Parse(json, CouncilProvider.Claude));
    }
}
