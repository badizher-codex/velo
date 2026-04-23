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
        UpdateCount();
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
        if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
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
