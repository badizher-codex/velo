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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            // Suppress known WebView2/COM transient errors, log everything
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };

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

            var mainWindow = new MainWindow(_services);
            MainWindow = mainWindow;
            mainWindow.Show();
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
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
