using VELO.Agent.Adapters;
using VELO.Agent.Models;
using Xunit;

namespace VELO.Agent.Tests;

public class AgentResponseParserTests
{
    // ── Plain-text fallback ───────────────────────────────────────────────────

    [Fact]
    public void PlainText_ReturnsTextOnlyResponse()
    {
        var result = AgentResponseParser.Parse("Hola, soy VeloAgent. ¿En qué te ayudo?");

        Assert.Equal("Hola, soy VeloAgent. ¿En qué te ayudo?", result.ReplyText);
        Assert.Empty(result.Actions);
        Assert.False(result.HasActions);
    }

    [Fact]
    public void InvalidJson_ReturnsTextOnlyResponse()
    {
        const string raw = "{ this is not valid json }";

        var result = AgentResponseParser.Parse(raw);

        Assert.False(result.HasActions);
        Assert.NotNull(result.ReplyText);
    }

    [Fact]
    public void EmptyString_ReturnsTextOnlyResponse()
    {
        var result = AgentResponseParser.Parse(string.Empty);

        Assert.False(result.HasActions);
    }

    // ── Structured JSON — reply only ──────────────────────────────────────────

    [Fact]
    public void StructuredJson_ReplyOnly_ReturnsCorrectText()
    {
        const string json = """{"reply":"Aquí tienes el resultado.","actions":[]}""";

        var result = AgentResponseParser.Parse(json);

        Assert.Equal("Aquí tienes el resultado.", result.ReplyText);
        Assert.Empty(result.Actions);
    }

    // ── Structured JSON — with actions ───────────────────────────────────────

    [Fact]
    public void StructuredJson_WithOpenTabAction_ParsedCorrectly()
    {
        const string json = """
            {
              "reply": "Abro una nueva pestaña con los resultados.",
              "actions": [
                {
                  "type": "OpenTab",
                  "description": "Abrir GitHub",
                  "url": "https://github.com"
                }
              ]
            }
            """;

        var result = AgentResponseParser.Parse(json);

        Assert.Equal("Abro una nueva pestaña con los resultados.", result.ReplyText);
        Assert.Single(result.Actions);
        Assert.Equal(AgentActionType.OpenTab, result.Actions[0].Type);
        Assert.Equal("Abrir GitHub", result.Actions[0].Description);
        Assert.Equal("https://github.com", result.Actions[0].Url);
        Assert.True(result.HasActions);
    }

    [Fact]
    public void StructuredJson_WithSearchAction_ParsedCorrectly()
    {
        const string json = """
            {
              "reply": "Buscando...",
              "actions": [
                { "type": "Search", "description": "Buscar VELO browser", "value": "VELO browser" }
              ]
            }
            """;

        var result = AgentResponseParser.Parse(json);

        Assert.Equal(AgentActionType.Search, result.Actions[0].Type);
        Assert.Equal("VELO browser", result.Actions[0].Value);
    }

    [Fact]
    public void StructuredJson_MultipleActions_AllParsed()
    {
        const string json = """
            {
              "reply": "Ejecutaré dos acciones.",
              "actions": [
                { "type": "OpenTab",  "description": "Abrir tab", "url": "https://a.com" },
                { "type": "Search",   "description": "Buscar algo", "value": "algo" }
              ]
            }
            """;

        var result = AgentResponseParser.Parse(json);

        Assert.Equal(2, result.Actions.Count);
        Assert.Equal(AgentActionType.OpenTab, result.Actions[0].Type);
        Assert.Equal(AgentActionType.Search,  result.Actions[1].Type);
    }

    [Fact]
    public void StructuredJson_UnknownActionType_IsSkipped()
    {
        const string json = """
            {
              "reply": "reply",
              "actions": [
                { "type": "UNKNOWN_ACTION_XYZ", "description": "desc" },
                { "type": "Search", "description": "valid", "value": "q" }
              ]
            }
            """;

        var result = AgentResponseParser.Parse(json);

        // Only the valid Search action survives
        Assert.Single(result.Actions);
        Assert.Equal(AgentActionType.Search, result.Actions[0].Type);
    }

    // ── Markdown fence stripping ──────────────────────────────────────────────

    [Fact]
    public void MarkdownFencedJson_IsStrippedAndParsed()
    {
        const string raw = "```json\n{\"reply\":\"ok\",\"actions\":[]}\n```";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("ok", result.ReplyText);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void MarkdownFencedJson_WithoutLanguageTag_IsStrippedAndParsed()
    {
        const string raw = "```\n{\"reply\":\"hola\",\"actions\":[]}\n```";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("hola", result.ReplyText);
    }

    // ── Optional action fields ────────────────────────────────────────────────

    [Fact]
    public void Action_OptionalFields_DefaultToNull()
    {
        const string json = """
            {"reply":"r","actions":[{"type":"Respond","description":"d"}]}
            """;

        var result = AgentResponseParser.Parse(json);

        var action = result.Actions[0];
        Assert.Null(action.Url);
        Assert.Null(action.Selector);
        Assert.Null(action.Value);
        Assert.Null(action.Text);
    }

    [Fact]
    public void Action_Id_IsEightCharsAndUnique()
    {
        const string json = """
            {"reply":"r","actions":[
              {"type":"Respond","description":"a"},
              {"type":"Respond","description":"b"}
            ]}
            """;

        var result = AgentResponseParser.Parse(json);

        Assert.All(result.Actions, a => Assert.Equal(8, a.Id.Length));
        Assert.NotEqual(result.Actions[0].Id, result.Actions[1].Id);
    }

    // ── AgentResponse static factories ───────────────────────────────────────

    [Fact]
    public void TextOnly_ReturnsNoActions()
    {
        var r = AgentResponse.TextOnly("mensaje");
        Assert.Equal("mensaje", r.ReplyText);
        Assert.Empty(r.Actions);
    }

    [Fact]
    public void Error_PrefixesMessage()
    {
        var r = AgentResponse.Error("timeout");
        Assert.StartsWith("Error:", r.ReplyText);
        Assert.Contains("timeout", r.ReplyText);
    }

    // ── CopyToClipboard / FillForm ────────────────────────────────────────────

    [Theory]
    [InlineData("CopyToClipboard", AgentActionType.CopyToClipboard)]
    [InlineData("FillForm",        AgentActionType.FillForm)]
    [InlineData("ClickElement",    AgentActionType.ClickElement)]
    [InlineData("ScrollTo",        AgentActionType.ScrollTo)]
    [InlineData("ReadPage",        AgentActionType.ReadPage)]
    [InlineData("Summarize",       AgentActionType.Summarize)]
    public void AllActionTypes_ParseCorrectly(string typeString, AgentActionType expected)
    {
        var json = $$"""{"reply":"r","actions":[{"type":"{{typeString}}","description":"d"}]}""";

        var result = AgentResponseParser.Parse(json);

        Assert.Single(result.Actions);
        Assert.Equal(expected, result.Actions[0].Type);
    }
}
