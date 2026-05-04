using System.Windows;
using System.Windows.Controls;
using VELO.Agent;
using VELO.Core.Localization;
using VELO.UI.Dialogs;

namespace VELO.UI.Controls;

/// <summary>
/// Phase 3 / Sprint 1E — Wraps the existing <see cref="ContextMenuBuilder"/>
/// (Phase 2) and decorates the menu with a 🤖 IA submenu before it's shown.
/// Uses composition rather than inheritance because ContextMenuBuilder's
/// build helpers are private — refactoring it virtual is parked for the
/// Sprint 7 refactor.
/// </summary>
public class AIContextMenuBuilder
{
    private readonly ContextMenuBuilder _inner;
    private readonly AIContextActions   _actions;

    /// <summary>Raised when an AI action is run and produces a result; host opens
    /// AIResultWindow with the supplied generator.</summary>
    public event EventHandler<AIActionInvocation>? AIActionRequested;

    public AIContextMenuBuilder(ContextMenuBuilder inner, AIContextActions actions)
    {
        _inner   = inner;
        _actions = actions;
    }

    public ContextMenu Build(ContextMenuContext ctx)
    {
        var menu = _inner.Build(ctx);

        // Append a separator + AI submenu so the existing menu structure
        // (which the user already knows from Fase 2) is preserved.
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());
        menu.Items.Add(BuildAISubmenu(ctx));
        return menu;
    }

    private MenuItem BuildAISubmenu(ContextMenuContext ctx)
    {
        var L      = LocalizationService.Current;
        var lang   = L.Language;
        var sub    = new MenuItem { Header = L.T("ctx.ai.menu") };

        // ── Text-selection actions ────────────────────────────────
        if (!string.IsNullOrWhiteSpace(ctx.SelectedText))
        {
            var text = ctx.SelectedText!;
            AddAction(sub, L.T("ctx.ai.text.explain"),  ctx, lang, text,
                ct => _actions.ExplainAsync(text, lang, ct));
            AddAction(sub, L.T("ctx.ai.text.summarize"), ctx, lang, text,
                ct => _actions.SummarizeAsync(text, 3, ct));
            AddAction(sub, L.T("ctx.ai.text.translate"), ctx, lang, text,
                ct => _actions.TranslateAsync(text, lang, ct));
            AddAction(sub, L.T("ctx.ai.text.factcheck"), ctx, lang, text,
                ct => _actions.FactCheckAsync(text, ct));
            AddAction(sub, L.T("ctx.ai.text.define"),    ctx, lang, text,
                ct => _actions.DefineAsync(text, ct));
            AddAction(sub, L.T("ctx.ai.text.eli5"),      ctx, lang, text,
                ct => _actions.SimplifyAsync(text, ct));
            sub.Items.Add(new Separator());
            AddListAction(sub, L.T("ctx.ai.text.extract.links"),  ctx, text, ExtractionKind.Links);
            AddListAction(sub, L.T("ctx.ai.text.extract.emails"), ctx, text, ExtractionKind.Emails);
            AddListAction(sub, L.T("ctx.ai.text.extract.phones"), ctx, text, ExtractionKind.Phones);
        }
        // ── Link actions ──────────────────────────────────────────
        else if (!string.IsNullOrEmpty(ctx.LinkUrl))
        {
            var link = ctx.LinkUrl!;
            AddAction(sub, L.T("ctx.ai.link.explain"), ctx, lang, link,
                ct => _actions.ExplainAsync($"URL: {link}\nLink text: {ctx.LinkText}", lang, ct));
            AddAction(sub, L.T("ctx.ai.link.preview"), ctx, lang, link,
                ct => _actions.SummarizeAsync($"Preview the page at {link}.", 3, ct));
        }
        // ── Image actions ─────────────────────────────────────────
        else if (ctx.HasImage)
        {
            var img = ctx.ImageUrl ?? "";
            AddAction(sub, L.T("ctx.ai.image.describe"), ctx, lang, img,
                ct => _actions.DescribeImageAsync([], ct));
            AddAction(sub, L.T("ctx.ai.image.ocr"),      ctx, lang, img,
                ct => _actions.OcrAsync([], ct));
        }
        // ── Page actions (no selection) ───────────────────────────
        else
        {
            AddAction(sub, L.T("ctx.ai.page.summarize"), ctx, lang, $"Page: {ctx.CurrentDomain}",
                ct => _actions.SummarizeAsync($"Page on {ctx.CurrentDomain}", 5, ct));
            AddAction(sub, L.T("ctx.ai.page.bullets"), ctx, lang, $"Page: {ctx.CurrentDomain}",
                ct => _actions.SummarizeAsync($"Page on {ctx.CurrentDomain}", 7, ct));
            AddAction(sub, L.T("ctx.ai.page.translate"), ctx, lang, $"Page: {ctx.CurrentDomain}",
                ct => _actions.TranslateAsync($"Page on {ctx.CurrentDomain}", lang, ct));
            AddAction(sub, L.T("ctx.ai.page.eli5"), ctx, lang, $"Page: {ctx.CurrentDomain}",
                ct => _actions.SimplifyAsync($"Page on {ctx.CurrentDomain}", ct));
        }

        return sub;
    }

    private void AddAction(MenuItem parent, string label, ContextMenuContext ctx,
                           string lang, string source,
                           Func<CancellationToken, Task<string>> generator)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => AIActionRequested?.Invoke(this,
            new AIActionInvocation(label, source, generator,
                                   _actions.AdapterName,
                                   IsCloudAdapter(_actions.AdapterName)));
        parent.Items.Add(item);
    }

    private void AddListAction(MenuItem parent, string label, ContextMenuContext ctx,
                               string text, ExtractionKind kind)
    {
        var item = new MenuItem { Header = label };
        item.Click += async (_, _) =>
        {
            var results = await _actions.ExtractAsync(text, kind);
            var formatted = results.Count == 0
                ? "(no matches)"
                : string.Join("\n", results.Select(r => "• " + r.Value));
            AIActionRequested?.Invoke(this, new AIActionInvocation(
                label, text, _ => Task.FromResult(formatted),
                _actions.AdapterName, IsCloudAdapter(_actions.AdapterName)));
        };
        parent.Items.Add(item);
    }

    /// <summary>True when the configured adapter sends data off-device.</summary>
    private static bool IsCloudAdapter(string name) =>
        name.Contains("Claude", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Cloud",  StringComparison.OrdinalIgnoreCase);

    public bool IsAISubmenuPresent(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Any(m => string.Equals(
            m.Header as string, LocalizationService.Current.T("ctx.ai.menu"),
            StringComparison.Ordinal));
}

/// <summary>
/// Payload raised by AIContextMenuBuilder when a menu item is clicked. The
/// host opens AIResultWindow.Show(...) with these fields.
/// </summary>
public record AIActionInvocation(
    string ActionLabel,
    string SourceContext,
    Func<CancellationToken, Task<string>> Generator,
    string AdapterName,
    bool   IsCloud);
