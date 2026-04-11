using System.Windows;
using VELO.Core.Downloads;
using VELO.Core.Localization;
using VELO.Data.Repositories;
using VELO.UI.Controls;

namespace VELO.UI.Dialogs;

public partial class ClearDataWindow : Window
{
    private readonly HistoryRepository _history;
    private readonly DownloadManager _downloads;
    private readonly IReadOnlyList<BrowserTab> _tabs;

    public ClearDataWindow(HistoryRepository history, DownloadManager downloads,
        IReadOnlyList<BrowserTab> tabs)
    {
        _history   = history;
        _downloads = downloads;
        _tabs      = tabs;
        InitializeComponent();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        Title = L.T("title.cleardata");
        TitleLabel.Text          = L.T("cleardata.title");
        SubtitleLabel.Text       = L.T("cleardata.subtitle");

        ClearHistoryCheck.Tag    = null; // prevent re-entrant
        ClearHistoryLabel.Text   = L.T("cleardata.history");
        ClearHistoryDesc.Text    = L.T("cleardata.history.desc");
        ClearCookiesLabel.Text   = L.T("cleardata.cookies");
        ClearCookiesDesc.Text    = L.T("cleardata.cookies.desc");
        ClearCacheLabel.Text     = L.T("cleardata.cache");
        ClearCacheDesc.Text      = L.T("cleardata.cache.desc");
        ClearDownloadsLabel.Text = L.T("cleardata.downloads");
        ClearDownloadsDesc.Text  = L.T("cleardata.downloads.desc");

        CancelButton.Content     = L.T("cleardata.cancel");
        ClearButton.Content      = L.T("cleardata.confirm");
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        var L = LocalizationService.Current;
        ClearButton.IsEnabled  = false;
        CancelButton.IsEnabled = false;

        bool doHistory   = ClearHistoryCheck.IsChecked   == true;
        bool doCookies   = ClearCookiesCheck.IsChecked   == true;
        bool doCache     = ClearCacheCheck.IsChecked     == true;
        bool doDownloads = ClearDownloadsCheck.IsChecked == true;

        if (!doHistory && !doCookies && !doCache && !doDownloads)
        {
            Close();
            return;
        }

        StatusLabel.Text       = L.T("cleardata.clearing");
        StatusLabel.Visibility = Visibility.Visible;

        try
        {
            if (doHistory)
                await _history.ClearAllAsync();

            if (doCookies || doCache)
                foreach (var tab in _tabs)
                    await tab.ClearBrowsingDataAsync(doCookies, doCache);

            if (doDownloads)
                _downloads.ClearCompleted();

            StatusLabel.Text = L.T("cleardata.done");
            await Task.Delay(900);
            Close();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
            ClearButton.IsEnabled  = true;
            CancelButton.IsEnabled = true;
        }
    }
}
