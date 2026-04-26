using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VELO.App.Startup;
using VELO.Data.Repositories;
using VELO.UI.Dialogs;
using VELO.Vault;

namespace VELO.App;

public partial class App : Application
{
    private IServiceProvider? _services;
    private SingleInstanceManager? _singleInstance;

    /// <summary>Extracts the first URL-like argument (http/https/file/ftp/velo).
    /// Anything else is ignored — protects against being launched with junk args.</summary>
    private static string? ExtractInitialUrl(string[] args)
    {
        foreach (var a in args)
        {
            if (string.IsNullOrWhiteSpace(a)) continue;
            if (Uri.TryCreate(a, UriKind.Absolute, out var u))
            {
                var s = u.Scheme.ToLowerInvariant();
                if (s is "http" or "https" or "file" or "ftp" or "velo")
                    return a;
            }
        }
        return null;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            // Suppress known WebView2/COM transient errors, log everything
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };

        // v2.0.5 — Single-instance + command-line URL handoff.
        // If another VELO is already running, forward the URL (if any) to it
        // and exit. The owner opens the URL in a new tab and pops to front.
        // This makes default-browser handoff (Bambu Studio update, "open in
        // browser" from any external program) work the way users expect.
        var initialUrl = ExtractInitialUrl(e.Args);
        _singleInstance = new SingleInstanceManager();
        if (!_singleInstance.IsFirstInstance)
        {
            if (!string.IsNullOrEmpty(initialUrl))
                _singleInstance.ForwardUrl(initialUrl);
            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        try
        {
            _services = DependencyConfig.Build();

            var bootstrapper = _services.GetRequiredService<AppBootstrapper>();
            await bootstrapper.InitializeAsync();

            var settings = _services.GetRequiredService<SettingsRepository>();
            var vault    = _services.GetRequiredService<VaultService>();

            if (!await bootstrapper.IsOnboardingCompletedAsync(settings))
            {
                var wizard = new OnboardingWizard(settings, vault);
                if (wizard.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            var mainWindow = new MainWindow(_services, initialUrl);
            MainWindow = mainWindow;
            mainWindow.Show();

            // Wire pipe → MainWindow once the window exists. Late-arriving
            // URLs from forwarded launches open as new tabs in this window.
            _singleInstance.UrlReceived += url =>
            {
                try
                {
                    if (Current.MainWindow is MainWindow mw)
                        mw.OpenUrlInNewTab(url);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to open forwarded URL in new tab: {Url}", url);
                }
            };
            _singleInstance.StartServer();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during startup");
            MessageBox.Show($"Error al iniciar VELO:\n{ex.Message}", "VELO — Error crítico",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.Dispose(); } catch { }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
