using VELO.Agent;
using Xunit;

namespace VELO.Agent.Tests;

public class CodeActionsTests
{
    private static (CodeActions Actions, List<(string Sys, string User)> Calls) Build(
        string reply = "OK")
    {
        var calls = new List<(string Sys, string User)>();
        var actions = new CodeActions
        {
            ChatDelegate = (sys, user, _) =>
            {
                calls.Add((sys, user));
                return Task.FromResult(reply);
            }
        };
        return (actions, calls);
    }

    // ── DetectLanguage ───────────────────────────────────────────────────

    [Theory]
    [InlineData("def hello():\n    print('hi')\nimport sys", "python")]
    [InlineData("fn main() -> i32 {\n    let x = 5;\n    let mut y = 7;\n    x\n}", "rust")]
    [InlineData("package main\nfunc add(a, b int) int {\n    x := a + b\n    return x\n}", "go")]
    [InlineData("public static void main(String[] args) {\n  System.out.println(\"x\");\n}", "java")]
    [InlineData("<?php echo $_GET['name']; ?>", "php")]
    [InlineData("SELECT * FROM users WHERE id = 1", "sql")]
    [InlineData("<!DOCTYPE html><html><body><div>x</div></body></html>", "html")]
    [InlineData("#!/bin/bash\necho \"$HOME\"", "bash")]
    [InlineData("function add(a, b) { console.log(a+b); }", "javascript")]
    [InlineData("interface Foo { name: string; age: number; }", "typescript")]
    [InlineData("#include <iostream>\nstd::cout << \"hi\";\nclass Foo {};", "cpp")]
    [InlineData("#include <stdio.h>\nint main() { return 0; }", "c")]
    public void DetectLanguage_RecognisesCommonLanguages(string code, string expected)
    {
        Assert.Equal(expected, CodeActions.DetectLanguage(code));
    }

    [Fact]
    public void DetectLanguage_UnknownReturnsCode()
    {
        Assert.Equal("code", CodeActions.DetectLanguage("just plain words here"));
    }

    [Fact]
    public void DetectLanguage_EmptyReturnsCode()
    {
        Assert.Equal("code", CodeActions.DetectLanguage(""));
        Assert.Equal("code", CodeActions.DetectLanguage("   "));
    }

    // ── LooksLikeCode ────────────────────────────────────────────────────

    [Fact]
    public void LooksLikeCode_RecognisesSymbolicDensity()
    {
        Assert.True(CodeActions.LooksLikeCode("function f(x) { return x * 2; }"));
        Assert.True(CodeActions.LooksLikeCode("if (a == b) { return; }"));
    }

    [Fact]
    public void LooksLikeCode_PlainProseIsFalse()
    {
        Assert.False(CodeActions.LooksLikeCode(
            "This is just a paragraph of regular prose that a human would write to another human."));
    }

    [Fact]
    public void LooksLikeCode_ShortStringIsFalse()
    {
        Assert.False(CodeActions.LooksLikeCode("hi"));
        Assert.False(CodeActions.LooksLikeCode(""));
    }

    [Fact]
    public void LooksLikeCode_IndentedMultilineIsCode()
    {
        var code = "def foo():\n    return 1\n    pass";
        Assert.True(CodeActions.LooksLikeCode(code));
    }

    // ── ExplainAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task Explain_PassesDetectedLanguageToPrompt()
    {
        var (actions, calls) = Build();
        await actions.ExplainAsync("def hello():\n    print('hi')\nimport sys");
        Assert.Single(calls);
        Assert.Contains("python", calls[0].User);
    }

    [Fact]
    public async Task Explain_EmptyInput_NoModelCall()
    {
        var (actions, calls) = Build();
        var result = await actions.ExplainAsync("");
        Assert.Empty(calls);
        Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── TranslateAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task Translate_IncludesBothLanguages()
    {
        var (actions, calls) = Build();
        await actions.TranslateAsync("function add(a,b){console.log(a+b);}", "python");
        Assert.Single(calls);
        // System prompt names both source (detected js) and target (python).
        Assert.Contains("javascript", calls[0].Sys);
        Assert.Contains("python",     calls[0].Sys);
    }

    [Fact]
    public async Task Translate_NoTargetLang_NoModelCall()
    {
        var (actions, calls) = Build();
        var result = await actions.TranslateAsync("def hi(): pass", "");
        Assert.Empty(calls);
        Assert.Contains("target language", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Debug / Optimize / Comment / AddErrorHandling ────────────────────

    [Fact]
    public async Task Debug_AsksForBugList()
    {
        var (actions, calls) = Build();
        await actions.DebugAsync("for (int i = 0; i <= arr.length; i++) {}");
        Assert.Single(calls);
        Assert.Contains("bug", calls[0].Sys, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Optimize_AsksForSuggestionsAndRewritten()
    {
        var (actions, calls) = Build();
        await actions.OptimizeAsync("def f(x):\n    return x");
        Assert.Single(calls);
        Assert.Contains("Suggestions", calls[0].Sys);
        Assert.Contains("Rewritten",   calls[0].Sys);
    }

    [Fact]
    public async Task Comment_PreservesCode()
    {
        var (actions, calls) = Build();
        await actions.CommentAsync("function f(x) { return x*2; }");
        Assert.Single(calls);
        Assert.Contains("WHY",  calls[0].Sys);
        Assert.Contains("WHAT", calls[0].Sys);
    }

    [Fact]
    public async Task AddErrorHandling_MentionsIdiomaticPattern()
    {
        var (actions, calls) = Build();
        await actions.AddErrorHandlingAsync("def parse(s): return int(s)");
        Assert.Single(calls);
        Assert.Contains("error handling", calls[0].Sys, StringComparison.OrdinalIgnoreCase);
    }

    // ── Truncation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Truncate_LongCodeStaysUnderMaxChars()
    {
        var (actions, calls) = Build();
        actions.MaxCodeChars = 100;
        var bigCode = new string('x', 5000);
        await actions.ExplainAsync(bigCode);
        Assert.Single(calls);
        // The user prompt embeds the truncated code with an ellipsis marker.
        Assert.Contains("truncated", calls[0].User);
        // Original 5000 'x's would be 5000 chars — confirm we cut down.
        Assert.True(calls[0].User.Length < 600);
    }

    // ── Fallback when no adapter ─────────────────────────────────────────

    [Fact]
    public async Task NoAdapter_ReturnsFallbackString()
    {
        var actions = new CodeActions(); // ChatDelegate stays null
        var result = await actions.ExplainAsync("function f() { return 1; }");
        Assert.Contains("```", result); // fallback wraps in code fence
    }

    [Fact]
    public async Task AdapterThrows_ReturnsFallback()
    {
        var actions = new CodeActions
        {
            ChatDelegate = (_, _, _) => throw new InvalidOperationException("boom")
        };
        var result = await actions.ExplainAsync("def hi(): pass");
        Assert.Contains("```", result);
    }
}
