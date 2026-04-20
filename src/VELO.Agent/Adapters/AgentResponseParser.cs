using System.Text.Json;
using VELO.Agent.Models;

namespace VELO.Agent.Adapters;

/// <summary>
/// Parses the structured JSON response from any agent backend.
/// Gracefully degrades to a text-only response on malformed JSON.
/// </summary>
internal static class AgentResponseParser
{
    internal static AgentResponse Parse(string raw)
    {
        try
        {
            // Strip markdown fences if present
            var json = raw.Trim();
            if (json.StartsWith("```")) json = json.Split('\n', 2)[1];
            if (json.EndsWith("```"))  json = json[..json.LastIndexOf("```")];
            json = json.Trim();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var reply = root.TryGetProperty("reply", out var r) ? r.GetString() ?? "" : "";

            var actions = new List<AgentAction>();
            if (root.TryGetProperty("actions", out var arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var typeStr = el.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (!Enum.TryParse<AgentActionType>(typeStr, true, out var actionType))
                        continue;

                    actions.Add(new AgentAction(
                        actionType,
                        el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                        el.TryGetProperty("url",         out var u) ? u.GetString() : null,
                        el.TryGetProperty("selector",    out var s) ? s.GetString() : null,
                        el.TryGetProperty("value",       out var v) ? v.GetString() : null,
                        el.TryGetProperty("text",        out var tx) ? tx.GetString() : null));
                }
            }

            return new AgentResponse(reply, actions.AsReadOnly());
        }
        catch
        {
            // Non-JSON reply — treat as plain text answer
            var cleaned = raw.Trim().TrimStart('`').TrimEnd('`');
            return AgentResponse.TextOnly(cleaned);
        }
    }
}
