using System.Windows;
using System.Windows.Controls;
using VELO.Agent;
using VELO.Core.Localization;
using VELO.UI.Dialogs;

namespace VELO.UI.Controls;

/// <summary>
/// Phase 3 / Sprint 1E — Wraps the existing <see cref="ContextMenuBuilder"/>
/// (Phase 2) and decorates the menu with AI actions inline before it's shown.
///
/// v2.4.15 — Switched from a nested 🤖 IA submenu to FLAT inline items.
/// The submenu never appeared in production (gate bug fixed in v2.4.14)
/// AND when the gate was finally fixed, the submenu was empty/unexpandable
/// in some WPF rendering paths, depending on style chain. Flat items
/// can't have that problem — they're either visible or the menu isn't
/// rendering at all (which is much more debuggable). Bonus: less
/// cognitive load — no hover-to-discover. The 4-6 most-relevant
/// AI actions for the current context appear directly under the
/// existing Phase 2 items.
///
/// Use composition (not inheritance) because ContextMenuBuilder's
/// build helpers are private; refactoring it virtual is parked for
/// the Sprint 7 refactor.
/// </summary>
public class AIContextMenuBuilder
{
    private readonly ContextMenuBuilder _inner;
    private readonly AIContextActions   _actions;
    private readonly CodeActions        _codeActions;

    /// <summary>Raised when an AI action is run and produces a result; host opens
    /// AIResultWindow with the supplied generator.</summary>
    public event EventHandler<AIActionInvocation>? AIActionRequested;

    public AIContextMenuBuilder(
        ContextMenuBuilder inner,
        AIContextActions   actions,
        CodeActions        codeActions)
    {
        _inner       = inner;
        _actions     = actions;
        _codeActions = codeActions;
    }

    public ContextMenu Build(ContextMenuContext ctx)
    {
        var menu = _inner.Build(ctx);

        // Append a separator + the inline AI actions for the current context.
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        AddInlineAIActions(menu, ctx);
        return menu;
    }

    private void AddInlineAIActions(ContextMenu menu, ContextMenuContext ctx)
    {
        var L      = LocalizationService.Current;
        var lang   = L.Language;
        var added  = 0;

        // ── Selection flow ─────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(ctx.SelectedText))
        {
            var text = ctx.SelectedText!;

            // v2.4.13 — Code-shaped selection gets dedicated actions BEFORE
            // the prose ones. Detection is pure (CodeActions.LooksLikeCode);
            // misses fall through to the regular text actions below.
            if (CodeActions.LooksLikeCode(text))
            {
                AddItem(menu, "💻 " + L.T("ctx.ai.code.explain"),       ctx, text,
                    ct => _codeActions.ExplainAsync(text, ct));
                AddItem(menu, "💻 " + L.T("ctx.ai.code.debug"),         ctx, text,
                    ct => _codeActions.DebugAsync(text, ct));
                AddItem(menu, "💻 " + L.T("ctx.ai.code.optimize"),      ctx, text,
                    ct => _codeActions.OptimizeAsync(text, ct));
                AddItem(menu, "💻 " + L.T("ctx.ai.code.comment"),       ctx, text,
                    ct => _codeActions.CommentAsync(text, ct));
                added += 4;
            }
            else
            {
                AddItem(menu, "🤖 " + L.T("ctx.ai.text.explain"),    ctx, text,
                    ct => _actions.ExplainAsync(text, lang, ct));
                AddItem(menu, "🤖 " + L.T("ctx.ai.text.summarize"),  ctx, text,
                    ct => _actions.SummarizeAsync(text, 3, ct));
                AddItem(menu, "🤖 " + L.T("ctx.ai.text.translate"),  ctx, text,
                    ct => _actions.TranslateAsync(text, lang, ct));
                AddItem(menu, "🤖 " + L.T("ctx.ai.text.define"),     ctx, text,
                    ct => _actions.DefineAsync(text, ct));
                added += 4;
            }
        }
        // ── Link flow ──────────────────────────────────────────────────
        else if (!string.IsNullOrEmpty(ctx.LinkUrl))
        {
            var link = ctx.LinkUrl!;
            AddItem(menu, "🤖 " + L.T("ctx.ai.link.explain"), ctx, link,
                ct => _actions.ExplainAsync($"URL: {link}\nLink text: {ctx.LinkText}", lang, ct));
            AddItem(menu, "🤖 " + L.T("ctx.ai.link.preview"), ctx, link,
                ct => _actions.SummarizeAsync($"Preview the page at {link}.", 3, ct));
            added += 2;
        }
        // ── Image flow ─────────────────────────────────────────────────
        else if (ctx.HasImage)
        {
            var img = ctx.ImageUrl ?? "";
            AddItem(menu, "🤖 " + L.T("ctx.ai.image.describe"), ctx, img,
                ct => _actions.DescribeImageAsync([], ct));
            AddItem(menu, "🤖 " + L.T("ctx.ai.image.ocr"),      ctx, img,
                ct => _actions.OcrAsync([], ct));
            added += 2;
        }
        // ── Page flow (no specific target) ─────────────────────────────
        else
        {
            AddItem(menu, "🤖 " + L.T("ctx.ai.page.summarize"), ctx, $"Page: {ctx.CurrentDomain}",
                ct => _actions.SummarizeAsync($"Page on {ctx.CurrentDomain}", 5, ct));
            AddItem(menu, "🤖 " + L.T("ctx.ai.page.translate"), ctx, $"Page: {ctx.CurrentDomain}",
                ct => _actions.TranslateAsync($"Page on {ctx.CurrentDomain}", lang, ct));
            AddItem(menu, "🤖 " + L.T("ctx.ai.page.eli5"),      ctx, $"Page: {ctx.CurrentDomain}",
                ct => _actions.SimplifyAsync($"Page on {ctx.CurrentDomain}", ct));
            added += 3;
        }

        Serilog.Log.Information(
            "AIContextMenuBuilder: added {Count} inline AI items (selection={HasSelection}, link={HasLink}, image={HasImage})",
            added,
            !string.IsNullOrWhiteSpace(ctx.SelectedText),
            !string.IsNullOrEmpty(ctx.LinkUrl),
            ctx.HasImage);
    }

    private void AddItem(ContextMenu menu, string label, ContextMenuContext ctx,
                         string source,
                         Func<CancellationToken, Task<string>> generator)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => AIActionRequested?.Invoke(this,
            new AIActionInvocation(label, source, generator,
                                   _actions.AdapterName,
                                   IsCloudAdapter(_actions.AdapterName)));
        menu.Items.Add(item);
    }

    /// <summary>True when the configured adapter sends data off-device.</summary>
    private static bool IsCloudAdapter(string name) =>
        name.Contains("Claude", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Cloud",  StringComparison.OrdinalIgnoreCase);

    /// <summary>v2.4.15 — kept for binary-compat with any caller still using
    /// the old "is the IA submenu present" check; always returns true now
    /// because the AI items are always added (even if just one).</summary>
    public bool IsAISubmenuPresent(ContextMenu menu) => true;
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
