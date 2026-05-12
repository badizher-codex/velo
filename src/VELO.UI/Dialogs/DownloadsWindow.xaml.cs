using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VELO.Core.Downloads;
using VELO.Core.Localization;

namespace VELO.UI.Dialogs;

public partial class DownloadsWindow : Window
{
    private readonly DownloadManager _manager;

    public DownloadsWindow(DownloadManager manager)
    {
        _manager = manager;
        InitializeComponent();
        DownloadList.ItemsSource = _manager.Downloads;
        _manager.Downloads.CollectionChanged += (_, _) => UpdateCount();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        Title = L.T("title.downloads");
        HeaderLabel.Text    = L.T("downloads.title");
        ClearBtn.Content    = L.T("downloads.clear");
        // Phase 5.0 — empty state copy. Key falls back to the Spanish literal
        // until downloads.empty.title / .subtitle are added to LocalizationService.
        EmptyTitle.Text    = ResolveOrFallback(L, "downloads.empty.title",    "No hay descargas aún");
        EmptySubtitle.Text = ResolveOrFallback(L, "downloads.empty.subtitle", "Las descargas que inicies aparecerán acá.");
        UpdateCount();
    }

    private static string ResolveOrFallback(LocalizationService L, string key, string fallback)
    {
        var v = L.T(key);
        return string.IsNullOrEmpty(v) || v == key ? fallback : v;
    }

    private void UpdateCount()
    {
        var L = LocalizationService.Current;
        var total = _manager.Downloads.Count;
        var active = _manager.Downloads.Count(d => d.IsInProgress);
        var fileWord = total == 1 ? L.T("downloads.file") : L.T("downloads.files");
        CountLabel.Text = active > 0
            ? $"{total} {fileWord} · {active} {L.T("downloads.inprogress")}"
            : $"{total} {fileWord}";
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        // v2.0.5.11 — Three-tier fallback so the button never feels broken:
        //   1) File on disk → /select highlights it in Explorer
        //   2) File missing but parent dir exists → open the parent dir
        //      (covers "still downloading" and "AV moved/quarantined the file")
        //   3) Neither → silently no-op
        if (sender is not Button btn || btn.Tag is not string path || string.IsNullOrEmpty(path)) return;

        if (File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", $"\"{dir}\"");
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var item = _manager.Downloads.FirstOrDefault(d => d.Id == id);
            if (item != null) _manager.Downloads.Remove(item);
        }
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        _manager.ClearCompleted();
        UpdateCount();
    }
}
