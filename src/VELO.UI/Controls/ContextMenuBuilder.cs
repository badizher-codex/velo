using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using VELO.Core.Navigation;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.UI.Utilities;

namespace VELO.UI.Controls;

/// <summary>
/// Builds the enriched VELO context menu for right-click events in WebView2.
/// Handles four contexts: Link, Image, Text selection, Page (no selection).
/// </summary>
public class ContextMenuBuilder(
    ContainerRepository containerRepository,
    BookmarkRepository bookmarkRepository,
    UrlCleaner urlCleaner,
    TabManager tabManager)
{
    private readonly ContainerRepository _containers = containerRepository;
    private readonly BookmarkRepository  _bookmarks  = bookmarkRepository;
    private readonly UrlCleaner          _urlCleaner = urlCleaner;
    private readonly TabManager          _tabManager = tabManager;

    public ContextMenu Build(ContextMenuContext ctx, Action? onPaste = null)
    {
        var menu = new ContextMenu();

        // v2.4.16 — Pegar at the top whenever the right-click target is an
        // editable element (input/textarea/contenteditable). Sits above the
        // contextual items so it's the first thing users see — most natural
        // place for it given mainstream-browser convention.
        // v2.4.19 — onPaste is supplied by the originating BrowserTab so the
        // paste lands ONLY in that tab. Was previously a broadcast event on
        // the singleton builder, which fired in every tab whose handler had
        // ever been wired (so a focused field in tab B received the paste
        // when the user pegó in tab A). Per-build callback fixes it by
        // construction.
        if (ctx.IsEditableTarget && onPaste is not null)
        {
            AddItem(menu, "📋 Pegar", onPaste);
            menu.Items.Add(new Separator());
        }

        if (ctx.LinkUrl is { } link)
            BuildLinkMenu(menu, link, ctx);
        else if (ctx.HasImage)
            BuildImageMenu(menu, ctx);
        else if (ctx.SelectedText is { Length: > 0 } text)
            BuildTextMenu(menu, text, ctx);
        else
            BuildPageMenu(menu, ctx);

        return menu;
    }

    // ── Link context ──────────────────────────────────────────────────────────

    private void BuildLinkMenu(ContextMenu menu, string linkUrl, ContextMenuContext ctx)
    {
        AddItem(menu, "Abrir enlace en nueva pestaña",   () => OpenInNewTab(linkUrl, ctx.CurrentContainerId));
        AddItem(menu, "Abrir enlace en nueva ventana",   () => OpenInNewWindow(linkUrl));
        AddContainerSubmenu(menu, linkUrl);
        AddItem(menu, "Abrir enlace en modo Incógnito",  () => OpenInContainer(linkUrl, "none"));
        AddItem(menu, "Abrir enlace con Glance (preview)", () => RequestGlance?.Invoke(linkUrl));
        menu.Items.Add(new Separator());

        AddItem(menu, "Copiar enlace",       () => Clipboard.SetText(linkUrl));
        AddItem(menu, "Copiar enlace limpio", () =>
        {
            var clean = _urlCleaner.Clean(linkUrl);
            Clipboard.SetText(clean);
        });
        AddItem(menu, "Copiar texto del enlace", () => Clipboard.SetText(ctx.LinkText ?? linkUrl));
        menu.Items.Add(new Separator());

        AddItem(menu, "🔒 Analizar seguridad del enlace",  () => RequestLinkAnalysis?.Invoke(linkUrl));
        AddItem(menu, "🔒 Verificar en Malwaredex",        () => RequestMalwaredexCheck?.Invoke(linkUrl));
        menu.Items.Add(new Separator());

        AddItem(menu, "Guardar como marcador", () => RequestBookmark?.Invoke(linkUrl, ctx.LinkText));
    }

    // ── Image context ─────────────────────────────────────────────────────────

    private void BuildImageMenu(ContextMenu menu, ContextMenuContext ctx)
    {
        var imageUrl = ctx.ImageUrl ?? "";

        AddItem(menu, "Abrir imagen en nueva pestaña", () => OpenInNewTab(imageUrl, ctx.CurrentContainerId));
        AddItem(menu, "Copiar URL de imagen",          () => Clipboard.SetText(imageUrl));
        menu.Items.Add(new Separator());
        AddItem(menu, "🔒 Analizar imagen (local)",    () => RequestImageAnalysis?.Invoke(imageUrl));
    }

    // ── Text selection context ────────────────────────────────────────────────

    private void BuildTextMenu(ContextMenu menu, string text, ContextMenuContext ctx)
    {
        AddItem(menu, "Copiar", () => Clipboard.SetText(text));

        var truncated = text.Length > 30 ? text[..30] + "…" : text;
        AddItem(menu, $"Buscar '{truncated}' en la web", () => RequestSearch?.Invoke(text));
        menu.Items.Add(new Separator());

        AddItem(menu, "🔒 Buscar sin rastreo (DuckDuckGo)",
            () => OpenInNewTab($"https://duckduckgo.com/?q={Uri.EscapeDataString(text)}", ctx.CurrentContainerId));
        AddItem(menu, "🔒 Preguntar a VeloAgent", () => RequestAgentPrompt?.Invoke(text));
    }

    // ── Page context ──────────────────────────────────────────────────────────

    private void BuildPageMenu(ContextMenu menu, ContextMenuContext ctx)
    {
        AddItem(menu, "Guardar página como…",           () => RequestSaveAs?.Invoke());
        AddItem(menu, "Imprimir…",                      () => RequestPrint?.Invoke());
        menu.Items.Add(new Separator());

        AddItem(menu, "🔒 Ver código fuente",            () => RequestViewSource?.Invoke());
        AddItem(menu, "🔒 Abrir DevTools (F12)",         () => RequestDevTools?.Invoke());
        AddItem(menu, "🔒 Abrir VELO Security Inspector", () => RequestSecurityInspector?.Invoke());
        AddItem(menu, "🔒 Ver Privacy Receipt",          () => RequestPrivacyReceipt?.Invoke());
        AddItem(menu, "🔒 Forzar re-análisis de IA",     () => RequestAIReanalysis?.Invoke());
        AddItem(menu, "🔒 \"Olvidar este sitio\"",        () => RequestForgetSite?.Invoke(ctx.CurrentDomain));
        menu.Items.Add(new Separator());

        AddItem(menu, "Leer en modo Reader (F9)",        () => RequestReaderMode?.Invoke());
    }

    // ── Container submenu ────────────────────────────────────────────────────

    private void AddContainerSubmenu(ContextMenu menu, string url)
    {
        var sub = new MenuItem { Header = "Abrir enlace en container ▶" };

        // Fire-and-forget: load containers async and populate
        _ = PopulateContainerSubmenuAsync(sub, url);

        menu.Items.Add(sub);
    }

    private async Task PopulateContainerSubmenuAsync(MenuItem sub, string url)
    {
        try
        {
            var containers = await _containers.GetAllAsync();
            foreach (var c in containers.Where(c => c.Id != "none"))
            {
                var captured = c;
                var item = new MenuItem { Header = captured.Name };
                item.Click += (_, _) => OpenInContainer(url, captured.Id);
                sub.Items.Add(item);
            }

            sub.Items.Add(new Separator());
            var tempItem = new MenuItem { Header = "+ Temporal" };
            tempItem.Click += (_, _) => RequestTemporaryContainer?.Invoke(url);
            sub.Items.Add(tempItem);
        }
        catch { /* silencioso si falla la carga de containers */ }
    }

    // ── Navigation helpers ────────────────────────────────────────────────────

    private void OpenInNewTab(string url, string containerId)
        => _tabManager.CreateTab(url, containerId);

    private static void OpenInNewWindow(string url)
        => RequestNewWindow?.Invoke(url);

    private void OpenInContainer(string url, string containerId)
        => _tabManager.CreateTab(url, containerId);

    // ── Static factory helper ────────────────────────────────────────────────

    private static MenuItem AddItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    // ── Callback hooks (set by BrowserTab or MainWindow) ─────────────────────
    public static event Action<string>? RequestNewWindow;

    public event Action<string>?        RequestGlance;
    public event Action<string>?        RequestLinkAnalysis;
    public event Action<string>?        RequestMalwaredexCheck;
    public event Action<string, string?>? RequestBookmark;
    public event Action<string>?        RequestImageAnalysis;
    public event Action<string>?        RequestSearch;
    public event Action<string>?        RequestAgentPrompt;
    public event Action?                RequestSaveAs;
    public event Action?                RequestPrint;
    public event Action?                RequestViewSource;
    public event Action?                RequestDevTools;
    public event Action?                RequestSecurityInspector;
    public event Action?                RequestPrivacyReceipt;
    public event Action?                RequestAIReanalysis;
    public event Action<string>?        RequestForgetSite;
    public event Action?                RequestReaderMode;
    public event Action<string>?        RequestTemporaryContainer;
}

// ── Context model ─────────────────────────────────────────────────────────────

public record ContextMenuContext(
    string?  LinkUrl,
    string?  LinkText,
    bool     HasImage,
    string?  ImageUrl,
    string?  SelectedText,
    string   CurrentDomain,
    string   CurrentContainerId,
    System.Windows.Point Location,
    bool     IsEditableTarget = false);
