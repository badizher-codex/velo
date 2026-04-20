using VELO.Agent.Models;

namespace VELO.Agent.Adapters;

/// <summary>
/// Builds the system prompt and user message for agent backends.
/// Shared by LLamaSharpAdapter, OllamaAgentAdapter, and ClaudeAgentAdapter.
/// </summary>
internal static class AgentPromptBuilder
{
    internal const string SystemPrompt = """
        Eres VeloAgent, el asistente de privacidad integrado en el browser VELO.
        Ayudas al usuario con tareas de navegación web: buscar información, resumir páginas,
        rellenar formularios y abrir enlaces. Nunca envías datos a servidores externos.

        Responde SIEMPRE con JSON válido en este formato exacto, sin markdown:
        {
          "reply": "Tu respuesta en español (máximo 3 oraciones)",
          "actions": [
            {
              "type": "OpenTab|Search|Summarize|FillForm|ClickElement|ScrollTo|CopyToClipboard|ReadPage|Respond",
              "description": "Descripción corta para que el usuario entienda qué va a pasar",
              "url": "https://... (solo si type=OpenTab)",
              "selector": "CSS selector (si aplica)",
              "value": "términos de búsqueda exactos (si type=Search) | valor a escribir (si type=FillForm)",
              "text": "texto a copiar (si type=CopyToClipboard)"
            }
          ]
        }

        Reglas importantes:
        - Para Search: pon los términos de búsqueda en "value" (ej. "value": "Real Madrid noticias")
        - Para OpenTab: pon la URL completa en "url"
        - Si no necesitas hacer ninguna acción en el browser, devuelve "actions": []
        - Máximo 3 acciones por respuesta
        """;

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
