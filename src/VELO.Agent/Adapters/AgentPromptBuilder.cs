using VELO.Agent.Models;
using VELO.Core.Localization;

namespace VELO.Agent.Adapters;

/// <summary>
/// Builds the system prompt and user message for agent backends.
/// Shared by LLamaSharpAdapter, OllamaAgentAdapter, and ClaudeAgentAdapter.
/// </summary>
internal static class AgentPromptBuilder
{
    /// <summary>
    /// v2.0.5.2 — Backwards-compat shim: reads the active UI language and
    /// returns a system prompt that instructs the model to reply in it.
    /// Adapters should prefer <see cref="GetSystemPrompt(string)"/> when they
    /// have an explicit language to honour (e.g. for testing).
    /// </summary>
    internal static string SystemPrompt => GetSystemPrompt(LocalizationService.Current.Language);

    /// <summary>Maps a language code to a prompt-friendly name shown to the model.</summary>
    private static string LanguageName(string lang) => lang switch
    {
        "es" => "Spanish",
        "en" => "English",
        "pt" => "Portuguese",
        "fr" => "French",
        "de" => "German",
        "zh" => "Chinese",
        "ru" => "Russian",
        "ja" => "Japanese",
        _    => "English",
    };

    internal static string GetSystemPrompt(string language)
    {
        var langName = LanguageName(language);

        // Prompt and JSON schema are kept in English so the model parses them
        // reliably across backends (LLamaSharp, Ollama, Claude). The reply
        // language is enforced explicitly in the rules and the schema example.
        return $$"""
            You are VeloAgent, the privacy assistant built into the VELO browser.
            You help the user with web tasks: searching, summarising pages,
            filling forms, and opening links. You never send data to external
            servers.

            Reply ALWAYS in {{langName}}. Even if the user writes in another
            language, your "reply" field MUST be written in {{langName}}.

            Reply with valid JSON ONLY (no markdown, no code fences) using this
            exact shape:
            {
              "reply": "Your answer in {{langName}} (max 3 sentences)",
              "actions": [
                {
                  "type": "OpenTab|Search|Summarize|FillForm|ClickElement|ScrollTo|CopyToClipboard|ReadPage|Respond",
                  "description": "Short description in {{langName}} so the user understands what will happen",
                  "url": "https://... (only when type=OpenTab)",
                  "selector": "CSS selector (when applicable)",
                  "value": "exact search terms (type=Search) | value to type (type=FillForm)",
                  "text": "text to copy (type=CopyToClipboard)"
                }
              ]
            }

            Rules:
            - For Search: put the exact search terms in "value".
            - For OpenTab: put the full URL in "url".
            - If no browser action is needed, return "actions": [].
            - Maximum 3 actions per reply.
            """;
    }

    internal static string BuildUserMessage(string userPrompt, AgentContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Contexto actual del browser:");
        sb.AppendLine($"- URL: {ctx.CurrentUrl}");
        sb.AppendLine($"- Título: {ctx.PageTitle}");
        sb.AppendLine($"- Container: {ctx.ContainerId}");
        sb.AppendLine($"- Pestañas abiertas: {ctx.OpenTabCount}");

        if (!string.IsNullOrEmpty(ctx.PageTextSnippet))
            sb.AppendLine($"- Contenido de la página (extracto):\n{ctx.PageTextSnippet[..Math.Min(ctx.PageTextSnippet.Length, 800)]}");

        sb.AppendLine();
        sb.AppendLine($"Usuario: {userPrompt}");
        return sb.ToString();
    }
}
