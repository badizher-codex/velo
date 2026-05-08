using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Agent;

/// <summary>
/// Phase 3 / Sprint 9A — Code-specific AI actions invoked from the
/// right-click menu when the user clicks on a <c>&lt;pre&gt;&lt;code&gt;</c>
/// block or a code-flavoured selection. Mirrors <see cref="AIContextActions"/>
/// in shape (ChatDelegate-driven, pure, testable) but speaks programmer.
///
/// Actions:
///   • <see cref="ExplainAsync"/>       — what does this code do, plain language
///   • <see cref="TranslateAsync"/>     — port to a different language
///   • <see cref="DebugAsync"/>         — find bugs, edge cases, off-by-ones
///   • <see cref="OptimizeAsync"/>      — perf or readability suggestions
///   • <see cref="CommentAsync"/>       — add inline comments without changing code
///   • <see cref="AddErrorHandlingAsync"/> — wrap in try/catch / error returns
///   • <see cref="DetectLanguage"/>     — best-effort language detection (pure)
///
/// Output convention: actions return ready-to-display markdown. Translate /
/// Comment / Optimize / AddErrorHandling include a fenced code block with
/// the rewritten source; Explain / Debug return plain prose.
/// </summary>
public sealed class CodeActions
{
    /// <summary>(systemPrompt, userPrompt, ct) → reply.</summary>
    public Func<string, string, CancellationToken, Task<string>>? ChatDelegate { get; set; }

    /// <summary>Maximum chars of code we forward to the model. Above this we truncate with a marker.</summary>
    public int MaxCodeChars { get; set; } = 4000;

    /// <summary>
    /// v2.4.17 — Two-letter locale code that natural-language replies (Explain,
    /// Debug, Optimize) should be in. Code itself stays in the source language;
    /// only the prose around it is localised. Host (MainWindow) sets this from
    /// LocalizationService.Current.Language at startup and re-sets it on
    /// LanguageChanged. Default "en" matches pre-v2.4.17 behaviour.
    /// </summary>
    public string ResponseLanguage { get; set; } = "en";

    private readonly ILogger<CodeActions> _logger;

    public CodeActions(ILogger<CodeActions>? logger = null)
    {
        _logger = logger ?? NullLogger<CodeActions>.Instance;
    }

    // ── Explain ──────────────────────────────────────────────────────────

    public Task<string> ExplainAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Task.FromResult("(empty selection)");

        var lang = DetectLanguage(code);
        var system =
            "You explain code to a developer who knows the basics. " +
            "Three to five sentences. Lead with the high-level intent, then " +
            "name the key data structures or libraries used. Don't restate " +
            "syntax (\"this is a for loop\"). Don't invent behaviour the code " +
            "doesn't show. " +
            $"Reply in {AIContextActions.LanguageName(ResponseLanguage)}.";

        var user = $"Language (detected): {lang}\n\nCode:\n```{lang}\n{Truncate(code)}\n```";
        return Chat(system, user, ct, fallback: $"```{lang}\n{Truncate(code)}\n```");
    }

    // ── Translate ────────────────────────────────────────────────────────

    public Task<string> TranslateAsync(string code, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Task.FromResult("(empty selection)");
        if (string.IsNullOrWhiteSpace(targetLang))
            return Task.FromResult("(no target language given)");

        var src    = DetectLanguage(code);
        var system =
            $"Port the {src} code to {targetLang}. Preserve behaviour exactly. " +
            "Output only a single fenced code block in the target language, " +
            "no preamble, no commentary. Use idiomatic patterns of the target " +
            "language. Do not invent imports the original didn't use.";

        var user = $"Source ({src}):\n```{src}\n{Truncate(code)}\n```";
        return Chat(system, user, ct, fallback: $"// translation unavailable offline\n{Truncate(code)}");
    }

    // ── Debug ────────────────────────────────────────────────────────────

    public Task<string> DebugAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult("(empty selection)");

        var lang = DetectLanguage(code);
        var system =
            "You audit code for bugs. List up to five concrete issues, each on " +
            "its own bullet. Format: '• <issue>: <one-line explanation>'. " +
            "Cover off-by-ones, null/None/undefined risks, type mismatches, " +
            "unhandled errors, race conditions and resource leaks. If the code " +
            "looks correct, say 'No obvious issues found' and stop. Do not " +
            "suggest stylistic changes here — that's for /optimize. " +
            $"Reply in {AIContextActions.LanguageName(ResponseLanguage)}.";

        var user = $"Language (detected): {lang}\n\nCode:\n```{lang}\n{Truncate(code)}\n```";
        return Chat(system, user, ct, fallback: "Bug analysis unavailable offline.");
    }

    // ── Optimize ─────────────────────────────────────────────────────────

    public Task<string> OptimizeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult("(empty selection)");

        var lang = DetectLanguage(code);
        var system =
            "Suggest performance and readability improvements. Output two " +
            "sections: 'Suggestions' (bulleted list) followed by 'Rewritten' " +
            "(a single fenced code block with the improved version). Don't " +
            "change behaviour, only how it's expressed. If nothing meaningful " +
            "can be improved, say so and skip the rewritten section. " +
            $"Write the suggestion text in {AIContextActions.LanguageName(ResponseLanguage)}; " +
            "keep code identifiers in their original language.";

        var user = $"Language (detected): {lang}\n\nCode:\n```{lang}\n{Truncate(code)}\n```";
        return Chat(system, user, ct, fallback: "Optimization suggestions unavailable offline.");
    }

    // ── Add comments ─────────────────────────────────────────────────────

    public Task<string> CommentAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult("(empty selection)");

        var lang = DetectLanguage(code);
        var system =
            "Add inline comments to the code. Comments should explain WHY, not " +
            "WHAT — assume the reader can read syntax. Don't change a single " +
            "character of the original code outside the comments. Output only " +
            "a single fenced code block, no preamble, no commentary. " +
            $"Write the comments in {AIContextActions.LanguageName(ResponseLanguage)}.";

        var user = $"Language (detected): {lang}\n\nCode:\n```{lang}\n{Truncate(code)}\n```";
        return Chat(system, user, ct, fallback: $"```{lang}\n{Truncate(code)}\n```");
    }

    // ── Add error handling ───────────────────────────────────────────────

    public Task<string> AddErrorHandlingAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult("(empty selection)");

        var lang = DetectLanguage(code);
        var system =
            "Wrap the code in idiomatic error handling for its language. " +
            "Try/catch where appropriate, error returns where the language " +
            "uses them (Go, Rust). Don't change non-error logic. Output only " +
            "a single fenced code block, no preamble. If you add error-message " +
            $"strings, write them in {AIContextActions.LanguageName(ResponseLanguage)}.";

        var user = $"Language (detected): {lang}\n\nCode:\n```{lang}\n{Truncate(code)}\n```";
        return Chat(system, user, ct, fallback: $"```{lang}\n{Truncate(code)}\n```");
    }

    // ── Pure helpers (testable without a model) ──────────────────────────

    /// <summary>
    /// Best-effort language detection from a few hundred chars of code.
    /// Returns canonical lowercased identifiers (csharp, javascript, python,
    /// rust, go, java, kotlin, swift, php, ruby, sql, html, css, bash,
    /// typescript, cpp, c). Defaults to "code" when no signal dominates.
    /// </summary>
    public static string DetectLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "code";
        var c = code; // keep casing for things like type signatures

        // Lowercased copy for keyword scans.
        var l = c.ToLowerInvariant();

        // Strong signals first — order matters because some keywords overlap.
        if (c.Contains("using System") || c.Contains("namespace ") || c.Contains("public class")
            || c.Contains("Console.WriteLine") || c.Contains("=>") && c.Contains(";"))
            if (l.Contains("using ") || l.Contains("namespace") || l.Contains("public class"))
                return "csharp";

        if (l.Contains("def ") && l.Contains(":") && (l.Contains("import ") || l.Contains("print(")))
            return "python";
        if (c.Contains("fn ") && c.Contains("->") && (c.Contains("let ") || c.Contains("mut ")))
            return "rust";
        if (c.Contains("func ") && c.Contains("package ") || (c.Contains("func ") && c.Contains(":=")))
            return "go";
        if (c.Contains("public static void main") || c.Contains("System.out.println"))
            return "java";
        if (c.Contains("fun ") && c.Contains(": ") && (c.Contains("val ") || c.Contains("var ")))
            return "kotlin";
        if (c.Contains("func ") && (c.Contains("var ") || c.Contains("let ")) && c.Contains("Swift"))
            return "swift";
        if (c.Contains("<?php") || c.Contains("$_GET") || c.Contains("$_POST"))
            return "php";
        if (c.Contains("require ") && (c.Contains("def ") || c.Contains("end")) || c.Contains("puts "))
            return "ruby";
        if (l.StartsWith("select ") || l.Contains("from ") && (l.Contains("where ") || l.Contains("join ")))
            return "sql";
        if (c.Contains("<!doctype") || c.Contains("<html") || c.Contains("</div>"))
            return "html";
        if (c.Contains("#!/bin/bash") || c.Contains("#!/usr/bin/env bash") || (l.Contains("echo ") && l.Contains("$")))
            return "bash";
        if (c.Contains("interface ") && c.Contains(": ") && c.Contains("=>") || c.Contains(": string") || c.Contains(": number"))
            return "typescript";
        if (c.Contains("function ") || c.Contains("const ") && c.Contains("=>") || c.Contains("console.log"))
            return "javascript";
        if (c.Contains("#include") && (c.Contains("std::") || c.Contains("class ") || c.Contains("template<")))
            return "cpp";
        if (c.Contains("#include") && c.Contains("int main"))
            return "c";
        if (c.Contains("{") && c.Contains(":") && c.Contains(";") && (c.Contains("display:") || c.Contains("color:")))
            return "css";

        return "code";
    }

    /// <summary>True when <paramref name="text"/> looks like source code rather than prose. Heuristic.</summary>
    public static bool LooksLikeCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length < 8) return false;

        // Density of "code-flavoured" characters relative to length.
        int symbolic = 0;
        foreach (var ch in text)
        {
            if (ch == '{' || ch == '}' || ch == '(' || ch == ')' ||
                ch == ';' || ch == '=' || ch == '<' || ch == '>' ||
                ch == '[' || ch == ']' || ch == '/' || ch == '*' ||
                ch == '$' || ch == '#') symbolic++;
        }
        var density = (double)symbolic / text.Length;

        // Indentation lines (multi-line + tab/space-prefixed lines) are a
        // strong signal even when symbol density is moderate.
        var lines = text.Split('\n');
        bool hasIndent = lines.Length > 1 &&
                        lines.Any(l => l.StartsWith("  ") || l.StartsWith("\t"));

        return density >= 0.06 || (hasIndent && density >= 0.03);
    }

    private string Truncate(string code)
        => code.Length <= MaxCodeChars
            ? code
            : code[..MaxCodeChars] + "\n// … (truncated; original is longer) …";

    private async Task<string> Chat(string system, string user, CancellationToken ct, string fallback)
    {
        if (ChatDelegate is null) return fallback;
        try
        {
            var reply = await ChatDelegate(system, user, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(reply) ? fallback : reply;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CodeActions chat call failed");
            return fallback;
        }
    }
}
