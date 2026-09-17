using System.Windows;
using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Enums;
using Microsoft.Win32;
using VMDesk.Rdp;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Forms.Integration;

namespace VMDesk.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IAppLog _log;
    private readonly RemoteSessionManager _sessions;
    private readonly IImportExportService _importExport;
    private readonly IBackupService _backup;
    private readonly IDiagnosticsService _diagnostics;
    private readonly ICredentialStore _credentials;
    private readonly RdpFileTransferService _transfer;
    private bool _sidebarCollapsed;
    private IRemoteSession? _embeddedSession;
    private WindowsFormsHost? _embeddedHost;

    public MainWindow(VmCatalogService catalog, ICredentialStore credentials, ISettingsService settings, RemoteSessionManager sessions, IImportExportService importExport, IBackupService backup, IDiagnosticsService diagnostics, RdpFileTransferService transfer, IAppLog log)
    {
        InitializeComponent();
        _log = log;
        _sessions = sessions;
        _credentials = credentials;
        _transfer = transfer;
        _importExport = importExport;
        _backup = backup;
        _diagnostics = diagnostics;
        _viewModel = new MainViewModel(catalog, credentials, settings);
        _viewModel.ConfirmDelete = vm => System.Windows.MessageBox.Show(this,
            $"Remove '{vm.Name}' from the library?\n\nThis removes only the library entry. The remote VM and saved credentials are not deleted.",
            "Remove VM", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        _viewModel.ActionFailed += (_, ex) =>
        {
            _log.Error("Could not complete VM removal or refresh the library.", ex);
            ShowError("VM removal failed", ex);
        };
        _viewModel.AddVmRequested += OnAddVmRequested;
        _viewModel.ConnectRequested += OnConnectRequested;
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            try
            {
                await _viewModel.LoadAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Could not load the VM library.", ex);
                System.Windows.MessageBox.Show(this, ex.Message, "VMDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private async void OnAddVmRequested(object? sender, EventArgs e)
    {
        var dialog = new AddVmWindow(_credentials) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var credential = string.IsNullOrWhiteSpace(dialog.CredentialReference) ? null : await _credentials.GetCredentialAsync(dialog.CredentialReference);
        await _viewModel.AddAsync(dialog.VmName, dialog.HostName, dialog.PortNumber, credential?.Username ?? dialog.UserName, dialog.CredentialReference, dialog.LaunchSeparateWindow, dialog.DriveRedirection);
        _log.Info($"Added VM '{dialog.VmName}'.");
    }

    private async void EditVmClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not VirtualMachineEntity vm) return;

        var dialog = new AddVmWindow(_credentials, vm) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var credential = string.IsNullOrWhiteSpace(dialog.CredentialReference) ? null : await _credentials.GetCredentialAsync(dialog.CredentialReference);
        vm.Name = dialog.VmName.Trim();
        vm.Host = dialog.HostName.Trim();
        vm.Port = dialog.PortNumber;
        vm.Username = credential?.Username ?? dialog.UserName;
        vm.CredentialReference = dialog.CredentialReference;
        vm.PreferredSessionDisplayMode = dialog.LaunchSeparateWindow ? SessionDisplayMode.SeparateWindow.ToString() : SessionDisplayMode.Embedded.ToString();
        vm.DriveRedirection = dialog.DriveRedirection;

        await _viewModel.UpdateAsync(vm);
        _log.Info($"Updated VM '{vm.Name}'.");
    }

    private async void OnConnectRequested(object? sender, VirtualMachineEntity vm)
    {
        try
        {
            var picker = new CredentialManagerWindow(_credentials) { Owner = this };
            var credentialReference = picker.ShowDialog() == true ? picker.SelectedCredential?.Reference : vm.CredentialReference;
            if (string.IsNullOrWhiteSpace(credentialReference)) return;
            var session = await _sessions.ConnectAsync(vm, credentialReference);
            if (string.Equals(vm.PreferredSessionDisplayMode, VMDesk.Core.Enums.SessionDisplayMode.SeparateWindow.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                new SessionWindow(session, _sessions, _transfer) { Owner = this }.Show();
            }
            else
            {
                ShowEmbeddedSession(session);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowEmbeddedSession(IRemoteSession session)
    {
        _embeddedSession = session;
        LibrarySurface.Visibility = Visibility.Collapsed;
        EmbeddedSessionSurface.Visibility = Visibility.Visible;
        EmbeddedTitle.Text = session.VmName;
        if (session.HostControl is System.Windows.Forms.Control control)
        {
            _embeddedHost = new WindowsFormsHost { Child = control };
            EmbeddedHost.Children.Add(_embeddedHost);
            session.HostMode = VMDesk.Core.Enums.SessionHostMode.Embedded;
        }
        else
        {
            EmbeddedStatus.Text = "RDP control unavailable on this Windows installation.";
        }
    }

    private void BackToLibraryClick(object sender, RoutedEventArgs e)
    {
        EmbeddedHost.Children.Clear();
        _embeddedHost = null;
        EmbeddedSessionSurface.Visibility = Visibility.Collapsed;
        LibrarySurface.Visibility = Visibility.Visible;
    }

    private async void ReconnectEmbeddedClick(object sender, RoutedEventArgs e)
    {
        if (_embeddedSession is not null) await _sessions.ReconnectAsync(_embeddedSession);
    }

    private async void DisconnectEmbeddedClick(object sender, RoutedEventArgs e)
    {
        if (_embeddedSession is not null) await _sessions.CloseAsync(_embeddedSession);
        _embeddedSession = null;
        BackToLibraryClick(sender, e);
    }

    private async void ScopeClick(object sender, RoutedEventArgs e) => await _viewModel.SetScopeAsync((string)((FrameworkElement)sender).Tag);

    private void CollapseSidebarClick(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = _sidebarCollapsed ? new GridLength(72) : new GridLength(236);
        SidebarText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarFooter.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SetMenuTextVisibility(SidebarMenu, _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible);
        SetMenuTextVisibility(SidebarFooterMenu, _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible);
        CollapseIcon.ToolTip = _sidebarCollapsed ? "Expand navigation" : "Collapse navigation";
        var chevron = CollapseIcon.Content as System.Windows.Shapes.Path;
        if (chevron is not null)
        {
            chevron.RenderTransform = new ScaleTransform(1, _sidebarCollapsed ? -1 : 1);
        }
    }

    private static void SetMenuTextVisibility(DependencyObject parent, Visibility visibility)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock text && !text.FontFamily.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase))
            {
                text.Visibility = visibility;
            }

            SetMenuTextVisibility(child, visibility);
        }
    }

    private void ThemeClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app && sender is FrameworkElement element && Enum.TryParse<VMDesk.Core.Enums.ThemeMode>((string)element.Tag, out var theme)) app.ApplyTheme(theme);
    }

    private void CredentialsClick(object sender, RoutedEventArgs e) => new CredentialManagerWindow(_credentials) { Owner = this }.ShowDialog();
    
        private void SettingsClick(object sender, RoutedEventArgs e) => new SettingsWindow((App)System.Windows.Application.Current) { Owner = this }.ShowDialog();

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "VMDesk export (*.json)|*.json", FileName = "vmdesk-export.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _importExport.ExportAsync(dialog.FileName, _viewModel.Vms.ToList());
        }
        catch (Exception ex)
        {
            ShowError("Export failed", ex);
        }
    }

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "VMDesk export (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _importExport.ImportAsync(dialog.FileName, ImportConflictStrategy.CreateNew);
            await _viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            ShowError("Import failed", ex);
        }
    }

    private async void BackupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await _backup.BackupAsync();
            System.Windows.MessageBox.Show(this, $"Backup created:\n{path}", "VMDesk", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Backup failed", ex);
        }
    }

    private async void DiagnosticsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = await _diagnostics.CollectAsync();
            new DiagnosticsWindow(info) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowError("Diagnostics failed", ex);
        }
    }

    private void ShowError(string title, Exception ex) =>
        System.Windows.MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}