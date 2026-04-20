using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace VELO.UI.Controls;

public partial class GlancePopup : UserControl
{
    private CoreWebView2Environment? _env;
    private bool _initialized;
    private string? _pendingUrl;

    public GlancePopup()
    {
        InitializeComponent();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Provide the shared WebView2 environment so the popup can initialize its mini view.</summary>
    public void SetEnvironment(CoreWebView2Environment env)
    {
        _env = env;
    }

    /// <summary>Navigate the preview to <paramref name="url"/> and make the popup visible.</summary>
    public async void ShowPreview(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        PreviewUrl.Text = url;

        if (!_initialized)
        {
            _pendingUrl = url;
            await EnsureInitializedAsync();
            return;
        }

        Visibility = Visibility.Visible;
        try
        {
            PreviewWebView.CoreWebView2.Navigate(url);
        }
        catch { /* WebView2 not ready yet — ignore */ }
    }

    /// <summary>Hides the popup and optionally stops loading.</summary>
    public void HidePreview()
    {
        Visibility = Visibility.Collapsed;

        // Stop loading to preserve bandwidth — user only glanced
        if (_initialized)
        {
            try { PreviewWebView.CoreWebView2.Stop(); }
            catch { }
        }
    }

    // ── Initialization ────────────────────────────────────────────────────

    private async Task EnsureInitializedAsync()
    {
        if (_initialized || _env == null) return;

        try
        {
            await PreviewWebView.EnsureCoreWebView2Async(_env);

            // Harden the preview: no scripting, no popups, no navigation outside preview
            var settings = PreviewWebView.CoreWebView2.Settings;
            settings.IsScriptEnabled              = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled           = false;
            settings.IsStatusBarEnabled           = false;
            settings.IsWebMessageEnabled          = false;

            // Block new-window attempts from the preview
            PreviewWebView.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

            _initialized = true;

            if (_pendingUrl != null)
            {
                Visibility = Visibility.Visible;
                PreviewWebView.CoreWebView2.Navigate(_pendingUrl);
                _pendingUrl = null;
            }
        }
        catch
        {
            _initialized = false;
        }
    }
}
