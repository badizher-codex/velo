using VELO.Agent;
using Xunit;

namespace VELO.Agent.Tests;

public class SlashCommandRouterTests
{
    private static (SlashCommandRouter Router, AIContextActions Actions, List<(string Sys, string User)> Calls)
        Build(string pageContent = "")
    {
        var calls = new List<(string Sys, string User)>();
        var actions = new AIContextActions
        {
            ChatDelegate = (sys, user, _) =>
            {
                calls.Add((sys, user));
                return Task.FromResult($"OK<<{user}>>");
            }
        };
        var router = new SlashCommandRouter(actions)
        {
            PageContentProvider = () => pageContent,
        };
        return (router, actions, calls);
    }

    // ── ParseInput ────────────────────────────────────────────────────────

    [Fact]
    public void ParseInput_NonSlash_ReturnsNullCmd()
    {
        var (cmd, args) = SlashCommandRouter.ParseInput("hello world");
        Assert.Null(cmd);
        Assert.Empty(args);
    }

    [Fact]
    public void ParseInput_BareCommand_LowercasesAndReturnsNoArgs()
    {
        var (cmd, args) = SlashCommandRouter.ParseInput("/TLDR");
        Assert.Equal("/tldr", cmd);
        Assert.Empty(args);
    }

    [Fact]
    public void ParseInput_CommandWithArgs_SplitsByWhitespace()
    {
        var (cmd, args) = SlashCommandRouter.ParseInput("/traducir en");
        Assert.Equal("/traducir", cmd);
        Assert.Equal(new[] { "en" }, args);
    }

    [Fact]
    public void ParseInput_CommandWithMultipleArgs_KeepsAllParts()
    {
        var (cmd, args) = SlashCommandRouter.ParseInput("/buscar precio del producto");
        Assert.Equal("/buscar", cmd);
        Assert.Equal(new[] { "precio", "del", "producto" }, args);
    }

    [Fact]
    public void IsSlashCommand_RecognisesLeadingSlash()
    {
        Assert.True(SlashCommandRouter.IsSlashCommand("/tldr"));
        Assert.True(SlashCommandRouter.IsSlashCommand("  /tldr 5"));
        Assert.False(SlashCommandRouter.IsSlashCommand("hola"));
        Assert.False(SlashCommandRouter.IsSlashCommand("/"));
        Assert.False(SlashCommandRouter.IsSlashCommand(""));
    }

    // ── Dispatch — spec § 7.4 #1 ──────────────────────────────────────────

    [Fact]
    public async Task SlashCommand_Tldr_InvokesSummarize()
    {
        var (router, _, calls) = Build(pageContent: "Página llena de texto sobre el tema X.");
        var result = await router.TryDispatchAsync("/tldr 3");
        Assert.NotNull(result);
        // Summarize uses ChatDelegate under the hood; verify it ran.
        Assert.Single(calls);
        Assert.Contains("texto sobre el tema X", calls[0].User);
    }

    [Fact]
    public async Task SlashCommand_Tldr_DefaultsTo5Lines_WhenArgMissing()
    {
        var (router, _, calls) = Build(pageContent: "ABC");
        await router.TryDispatchAsync("/tldr");
        Assert.Single(calls);
        // Summarize embeds the line count in the SYSTEM prompt, not user.
        Assert.Contains("5 lines", calls[0].Sys);
    }

    // ── Spec § 7.4 #2 ─────────────────────────────────────────────────────

    [Fact]
    public async Task SlashCommand_Unknown_FallsBackToGeneralChat()
    {
        var (router, _, _) = Build();
        var result = await router.TryDispatchAsync("/no-such-command");
        Assert.Null(result); // null = caller should treat as free-form chat
    }

    [Fact]
    public async Task NonSlashInput_ReturnsNull()
    {
        var (router, _, _) = Build();
        Assert.Null(await router.TryDispatchAsync("¿qué es esto?"));
    }

    // ── Translate ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Translate_UsesProvidedLang()
    {
        var (router, _, calls) = Build(pageContent: "Hola mundo");
        await router.TryDispatchAsync("/traducir en");
        Assert.Single(calls);
        // TranslateAsync embeds the resolved language NAME (English/Spanish/…)
        // in the system prompt, not the code.
        Assert.Contains("English", calls[0].Sys);
        Assert.Equal("Hola mundo", calls[0].User);
    }

    [Fact]
    public async Task Translate_FallsBackToDefaultLang_WhenArgMissing()
    {
        var (router, _, calls) = Build(pageContent: "Some content");
        router.DefaultTranslateLang = "es";
        await router.TryDispatchAsync("/traducir");
        Assert.Single(calls);
        Assert.Contains("Spanish", calls[0].Sys);
    }

    // ── Find / search ─────────────────────────────────────────────────────

    [Fact]
    public void SearchInPage_ReturnsContextSnippet()
    {
        var content = new string('a', 200) + " HELLO world " + new string('b', 200);
        var result = SlashCommandRouter.SearchInPage(content, "hello");
        Assert.Contains("HELLO world", result);
        Assert.StartsWith("1 coincidencia", result);
    }

    [Fact]
    public void SearchInPage_NoMatch_ReturnsFriendlyMessage()
    {
        var result = SlashCommandRouter.SearchInPage("nothing relevant", "missing");
        Assert.Contains("No se encontr", result);
    }

    [Fact]
    public async Task Search_RequiresQuery_OtherwiseShowsUsage()
    {
        var (router, _, _) = Build(pageContent: "abc");
        var result = await router.TryDispatchAsync("/buscar");
        Assert.Contains("Uso:", result!);
    }

    // ── Extract ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Extract_RecognisesEmails()
    {
        var (router, _, _) = Build(pageContent: "Contacto: hi@example.com y soporte@vendor.io");
        var result = await router.TryDispatchAsync("/extraer emails");
        Assert.Contains("hi@example.com", result!);
        Assert.Contains("soporte@vendor.io", result!);
    }

    [Fact]
    public async Task Extract_UnknownKind_ShowsUsage()
    {
        var (router, _, _) = Build(pageContent: "abc");
        var result = await router.TryDispatchAsync("/extraer pancakes");
        Assert.Contains("Uso:", result!);
    }

    // ── Help ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Help_ListsAllCommands()
    {
        var (router, _, _) = Build();
        var help = await router.TryDispatchAsync("/help");
        Assert.NotNull(help);
        Assert.Contains("/tldr", help);
        Assert.Contains("/traducir", help);
        Assert.Contains("/extraer", help);
    }

    // ── Empty page guard ──────────────────────────────────────────────────

    [Fact]
    public async Task EmptyPage_ReturnsFriendlyMessage_NotCrash()
    {
        var (router, _, calls) = Build(pageContent: "");
        var result = await router.TryDispatchAsync("/tldr");
        Assert.NotNull(result);
        Assert.Empty(calls); // no model call when we know there's nothing to summarise
        Assert.Contains("No hay contenido", result);
    }
}
