using System.Windows;
using VMDesk.Application.Services;
using VMDesk.Infrastructure.Configuration;
using VMDesk.Infrastructure.CredentialStore;
using VMDesk.Infrastructure.Logging;
using VMDesk.Infrastructure.Persistence;
using VMDesk.Infrastructure.Diagnostics;
using VMDesk.App.Views;
using VMDesk.Rdp;
using VMDesk.Core.Enums;

namespace VMDesk.App;

/// <summary>
/// Entry point for the VMDesk desktop application (spec §1, §5).
/// </summary>
public partial class App : System.Windows.Application
{
    private FileLogFactory? _logFactory;

    public void ApplyTheme(VMDesk.Core.Enums.ThemeMode mode)
    {
        var source = mode switch
        {
            VMDesk.Core.Enums.ThemeMode.Light => "Resources/Light.xaml",
            VMDesk.Core.Enums.ThemeMode.Dark => "Resources/Dark.xaml",
            _ => "Resources/Ocean.xaml"
        };
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppPaths.EnsureDirectories();
            _logFactory = new FileLogFactory(AppPaths.LogsDirectory, VMDesk.Core.Enums.LogLevelOption.Information);
            var log = _logFactory.GetLogger("Startup");
            var factory = new VmDeskDbContextFactory(AppPaths.DatabaseFile);
            var bootstrapper = new DatabaseBootstrapper(factory, AppPaths.DatabaseFile, _logFactory);
            var result = await bootstrapper.BootstrapAsync();
            if (!result.Success)
            {
                System.Windows.MessageBox.Show(result.Error ?? "The VMDesk database could not be opened.", "VMDesk", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var repository = new VmRepository(factory, _logFactory);
            var settings = new SqliteSettingsService(factory, _logFactory);
            var credentials = new WindowsCredentialStore(_logFactory);
            var catalog = new VmCatalogService(repository, credentials, _logFactory);
            var engine = new MicrosoftRdpEngine(credentials, _logFactory);
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
            var window = new MainWindow(catalog, credentials, settings, sessions, importExport, backup, diagnostics, transfer, log);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "VMDesk startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logFactory?.Flush();
        base.OnExit(e);
    }
}
