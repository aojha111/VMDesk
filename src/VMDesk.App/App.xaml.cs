using System.Windows;
using VMDesk.Application.Services;
using VMDesk.Infrastructure.Configuration;
using VMDesk.Infrastructure.CredentialStore;
using VMDesk.Infrastructure.Logging;
using VMDesk.Infrastructure.Persistence;
using VMDesk.Infrastructure.Diagnostics;
using VMDesk.App.Views;
using VMDesk.Rdp;
using AppThemeMode = VMDesk.Core.Enums.ThemeMode;

namespace VMDesk.App;

/// <summary>
/// Entry point for the VMDesk desktop application (spec §1, §5).
/// </summary>
public partial class App : System.Windows.Application
{
    private FileLogFactory? _logFactory;

    private VMDesk.Core.Interfaces.ISettingsService? _settings;
    public AppThemeMode CurrentTheme { get; private set; } = AppThemeMode.System;

    public void ApplyTheme(AppThemeMode mode)
    {
        var dark = mode == AppThemeMode.Dark;
        if (mode == AppThemeMode.System)
        {
            try
            {
                dark = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1) is int value && value == 0;
            }
            catch (System.Security.SecurityException) { dark = false; }
            catch (UnauthorizedAccessException) { dark = false; }
        }
        Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri(dark ? "Resources/Themes/Dark.xaml" : "Resources/Themes/Light.xaml", UriKind.Relative)
        };
        CurrentTheme = mode;
    }

    public async Task SaveThemeAsync(AppThemeMode mode)
    {
        if (_settings is null) throw new InvalidOperationException("Settings are not available yet.");
        var settings = await _settings.GetAsync();
        settings.Theme = mode;
        await _settings.SaveAsync(settings);
        ApplyTheme(mode);
    }

protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up unhandled exception handlers
            DispatcherUnhandledException += (s, args) =>
            {
                _logFactory?.GetLogger("Crash").Error("Unhandled UI exception: " + args.Exception);
                System.Windows.MessageBox.Show(args.Exception.ToString(), "VMDesk Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                _logFactory?.GetLogger("Crash").Error("Unhandled exception: " + args.ExceptionObject);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                _logFactory?.GetLogger("Crash").Error("Unobserved task exception: " + args.Exception);
                args.SetObserved();
            };

            try
            {
                AppPaths.EnsureDirectories();
                _logFactory = new FileLogFactory(AppPaths.LogsDirectory, VMDesk.Core.Enums.LogLevelOption.Information);
                var log = _logFactory.GetLogger("Startup");
                log.Info("Starting VMDesk...");
                
                var factory = new VmDeskDbContextFactory(AppPaths.DatabaseFile);
                var bootstrapper = new DatabaseBootstrapper(factory, AppPaths.DatabaseFile, _logFactory);
                var result = await bootstrapper.BootstrapAsync();
                if (!result.Success)
                {
                    log.Error("Database bootstrap failed: " + result.Error);
                    System.Windows.MessageBox.Show(result.Error ?? "The VMDesk database could not be opened.", "VMDesk", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1);
                    return;
                }
                log.Info("Database bootstrapped successfully");

                var repository = new VmRepository(factory, _logFactory);
                var settings = new SqliteSettingsService(factory, _logFactory);
                _settings = settings;
                log.Info("Settings service created");
                
                ApplyTheme((await settings.GetAsync()).Theme);
                log.Info("Theme applied");
                
                var credentials = new WindowsCredentialStore(_logFactory);
                log.Info("Credential store created");
                
                var catalog = new VmCatalogService(repository, credentials, _logFactory);
                log.Info("Catalog service created");
                
                var engine = new MicrosoftRdpEngine(credentials, _logFactory);
                log.Info("RDP engine created");
                
                var importExport = new ImportExportService(repository, _logFactory);
                var backup = new BackupRestoreService(() => AppPaths.DatabaseFile, _logFactory);
                var diagnostics = new DiagnosticsService(
                    () => AppPaths.DatabaseFile,
                    engine.CheckAvailabilityAsync,
                    credentials.IsAvailableAsync,
                    _logFactory);
                var transfer = new RdpFileTransferService(_logFactory);
                var orchestrator = new ConnectionOrchestrator(_logFactory);
                var sessions = new RemoteSessionManager(engine, orchestrator, _logFactory);
                log.Info("Services created, creating MainWindow...");
                
                var window = new MainWindow(catalog, credentials, settings, sessions, importExport, backup, diagnostics, transfer, log);
                MainWindow = window;
                log.Info("MainWindow created, showing...");
                window.Show();
                log.Info("MainWindow shown");
            }
            catch (Exception ex)
            {
                _logFactory?.GetLogger("Startup").Error("Startup failed: " + ex);
                System.Windows.MessageBox.Show(ex.ToString(), "VMDesk startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

    protected override void OnExit(ExitEventArgs e)
    {
        _logFactory?.Flush();
        base.OnExit(e);
    }
}
