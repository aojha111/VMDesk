using System.Diagnostics;
using System.Windows;
using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Enums;
using VMDesk.Core.Models;
using Microsoft.Win32;
using VMDesk.Rdp;
using VMDesk.Rdp.Interop;
using VMDesk.Infrastructure.Logging;
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
    private readonly DiscoveryService _discovery;
    private bool _sidebarCollapsed;
    private IRemoteSession? _embeddedSession;
    private WindowsFormsHost? _embeddedHost;
    private IRemoteSession? _externalStatusSession;
    private bool _isConnecting;

    /// <summary>Hypervisor discovery aggregation; the scan UI (Task 7) invokes this.</summary>
    public DiscoveryService Discovery => _discovery;

    public MainWindow(VmCatalogService catalog, ICredentialStore credentials, ISettingsService settings, RemoteSessionManager sessions, IImportExportService importExport, IBackupService backup, IDiagnosticsService diagnostics, RdpFileTransferService transfer, IAppLog log, DiscoveryService discovery)
    {
        InitializeComponent();
        _log = log;
        _sessions = sessions;
        _credentials = credentials;
        _transfer = transfer;
        _importExport = importExport;
        _backup = backup;
        _diagnostics = diagnostics;
        _discovery = discovery;
        _viewModel = new MainViewModel(catalog, credentials, settings, discovery);
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
        _viewModel.DiscoveryFailed += (_, ex) =>
        {
            _log.Error("Discovery scan failed.", ex);
            ShowError("Discovery failed", ex);
        };
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

            // One automatic discovery scan at startup (spec §27). Queued on the dispatcher so
            // the window paints and the catalog renders immediately: the scan never delays it.
            _ = Dispatcher.InvokeAsync(RunStartupDiscoveryAsync, System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    private async Task RunStartupDiscoveryAsync()
    {
        try
        {
            await _viewModel.DiscoverAsync();
        }
        catch (Exception ex)
        {
            // DiscoverCommand already routes its own failures here; this covers the auto-run.
            _log.Error("Automatic discovery scan failed.", ex);
        }
    }

    private async void OnAddVmRequested(object? sender, EventArgs e)
    {
        var dialog = new AddVmWindow(_credentials) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var credential = string.IsNullOrWhiteSpace(dialog.CredentialReference) ? null : await _credentials.GetCredentialAsync(dialog.CredentialReference);
        await _viewModel.AddAsync(dialog.VmName, dialog.HostName, dialog.PortNumber, credential?.Username ?? dialog.UserName, dialog.CredentialReference, dialog.LaunchSeparateWindow, dialog.DriveRedirection);
        _log.Info($"Added VM '{dialog.VmName}'.");
    }

    private void EditVmClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not VirtualMachineEntity vm) return;
        _ = EditVmAsync(vm);
    }

    private async Task EditVmAsync(VirtualMachineEntity vm)
    {
        var dialog = new AddVmWindow(_credentials, vm) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var credential = string.IsNullOrWhiteSpace(dialog.CredentialReference) ? null : await _credentials.GetCredentialAsync(dialog.CredentialReference);
        vm.Name = dialog.VmName.Trim();
        vm.Host = dialog.HostName.Trim();
        vm.Port = dialog.PortNumber ?? 0; // 0 = use the RDP default port (3389).
        vm.Username = credential?.Username ?? dialog.UserName;
        vm.CredentialReference = dialog.CredentialReference;
        vm.PreferredSessionDisplayMode = dialog.LaunchSeparateWindow ? SessionDisplayMode.SeparateWindow.ToString() : SessionDisplayMode.Embedded.ToString();
        vm.DriveRedirection = dialog.DriveRedirection;

        await _viewModel.UpdateAsync(vm);
        _log.Info($"Updated VM '{vm.Name}'.");
    }

    /// <summary>
    /// Discovered Hyper-V rows without an RDP host connect through the local console
    /// (vmconnect.exe + provider id) instead of failing on the missing address (Task 7).
    /// </summary>
    private static bool UsesHyperVConsole(VirtualMachineEntity vm) =>
        string.IsNullOrWhiteSpace(vm.Host)
        && vm.Provider.Equals("HyperV", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(vm.ProviderId);

    /// <summary>
    /// A discovered VM with neither RDP host nor console path must explain itself, not
    /// silently no-op: the dialog offers to open the editor where the host can be set.
    /// </summary>
    private void ShowMissingAddressDialog(VirtualMachineEntity vm)
    {
        var choice = System.Windows.MessageBox.Show(this,
            $"No RDP address found for this VM — enable Enhanced Session or set the host in Edit.\n\nOpen the editor for '{vm.Name}'?",
            "Cannot connect",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        if (choice == MessageBoxResult.Yes)
        {
            _ = EditVmAsync(vm);
        }
    }

    /// <summary>Which external client owns the session window — mstsc or the Hyper-V console.</summary>
    private static string DescribeExternalSession(IRemoteSession session) =>
        session is VMDesk.Rdp.VmConnectExternalSession
            ? "Console opened in Hyper-V Virtual Machine Connection — its state is shown by that window."
            : "Session opened in Windows Remote Desktop — its state is shown by the Remote Desktop client.";

    private async void OnConnectRequested(object? sender, VirtualMachineEntity vm)
    {
        if (_isConnecting) return;
        _isConnecting = true;
        try
        {
            await ConnectAsync(vm);
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private async Task ConnectAsync(VirtualMachineEntity vm)
    {
        try
        {
            // Task 7 connect routing for VMs without an RDP address: discovered Hyper-V VMs
            // open the local console via vmconnect.exe; anything else gets an actionable
            // error instead of a silent no-op or a confusing dial failure.
            var hyperVConsole = UsesHyperVConsole(vm);
            if (string.IsNullOrWhiteSpace(vm.Host) && !hyperVConsole)
            {
                ShowMissingAddressDialog(vm);
                return;
            }

            if (hyperVConsole && VmConnectLocator.TryGetFullPath() is null)
            {
                System.Windows.MessageBox.Show(this,
                    "The Hyper-V console client (vmconnect.exe) was not found on this PC. " +
                    "Install the Hyper-V management tools, or set the host in Edit to connect over RDP.",
                    "Console unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // RDP availability first: fail fast with an actionable message. The engine
            // falls back to an external mstsc.exe session when the ActiveX control
            // cannot be instantiated, so only stop when that fallback is missing too.
            // A console connect needs neither: vmconnect is its own client.
            var availability = await _sessions.CheckEngineAvailabilityAsync();
            if (!hyperVConsole && !availability.Available && MstscLocator.TryGetFullPath() is null)
            {
                System.Windows.MessageBox.Show(this,
                    $"The Microsoft Remote Desktop ActiveX control is not available.\n\nDetails: {availability.Details}\n\nPlease install the Remote Desktop Connection client or run Diagnostics for more information.",
                    "RDP Component Missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Resolve the credential: the VM's saved reference first, otherwise let
            // the user pick one from the dropdown of saved credentials. Editing
            // stays in the Credential Manager page (spec §7). On the external mstsc
            // path no pick is needed: mstsc.exe prompts for credentials itself and
            // passwords are never passed on its command line.
            var credentialReference = vm.CredentialReference;
            if (!hyperVConsole &&
                availability.Available &&
                (string.IsNullOrWhiteSpace(credentialReference) ||
                await _credentials.GetCredentialAsync(credentialReference) is null))
            {
                var picker = new CredentialPickerWindow(_credentials, credentialReference) { Owner = this };
                if (picker.ShowDialog() != true || picker.SelectedCredential is null)
                {
                    return; // User cancelled the pick; do not connect.
                }

                credentialReference = picker.SelectedCredential.Reference;
                if (!string.Equals(vm.CredentialReference, credentialReference, StringComparison.Ordinal))
                {
                    vm.CredentialReference = credentialReference;
                    await _viewModel.UpdateAsync(vm); // Remember the assignment for next time.
                }
            }

            // A leftover failed/dropped session for this VM would block a fresh
            // attempt, and an already-connected one just needs activation.
            var previous = _sessions.FindByVm(vm.Id);
            if (previous is not null && previous.State == VMDesk.Core.Enums.ConnectionState.Connected)
            {
                previous.Activate();
                OpenSessionSurface(previous, vm);
                return;
            }

            // Non-modal progress window: the await below continues on the UI
            // thread while the dialog animates, and it is closed as soon as the
            // connect attempt finishes (the previous modal version deadlocked
            // the flow because ShowDialog blocks until the user closes it).
            var progressDialog = new ConnectionProgressWindow { Owner = this };
            progressDialog.SetTitle(vm.Name);
            var progress = new Progress<string>(msg => progressDialog.UpdateStatus(msg));

            // Live cancel: the dialog's Cancel button cancels this token, the token
            // is linked into the orchestrator's per-attempt timeout, and it reaches
            // session.ConnectAsync — the dial is abandoned, not just the UI.
            using var cts = new CancellationTokenSource();
            progressDialog.CancelRequested += (_, _) =>
            {
                if (!cts.IsCancellationRequested)
                {
                    _log.Info($"User cancelled the connection to '{vm.Name}'.");
                }

                try { cts.Cancel(); } catch (ObjectDisposedException) { /* dial already finished */ }
            };
            progressDialog.Show();

            // Parent-before-connect: surfaceReady runs right after the control is
            // prepared and BEFORE the dial starts, so the AxHost is already inside a
            // visible window when Connect() fires. Re-parenting later recreates the
            // control's HWND and kills the handshake.
            // vmconnect owns its own window; it always surfaces through the embedded
            // external-client panel, never the SessionWindow that expects a control.
            var separateWindow = !hyperVConsole && SessionLaunchResolver.IsSeparateWindow(vm);
            SessionWindow? standalone = null;

            IRemoteSession session;
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                session = await _sessions.ConnectAsync(vm, credentialReference, progress, surfaceReady: async s =>
                {
                    if (separateWindow)
                    {
                        standalone = new SessionWindow(s, _sessions, _transfer) { Owner = this };
                        standalone.Show();
                        // Loaded is a dispatcher event queued at DispatcherPriority.
                        // Loaded; whether it has already drained when Show() returns is
                        // a WPF implementation detail we must not rely on. Pumping the
                        // dispatcher to that priority guarantees SessionWindow.OnLoaded
                        // parents the prepared control (and sets HostMode) BEFORE this
                        // callback returns and the manager dials; without the yield the
                        // ordering would rely on the credential await happening to yield
                        // first (WindowsCredentialStore.GetCredentialAsync's Task.Run).
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
                        Debug.Assert(s.HostControl is not System.Windows.Forms.Control || standalone.IsControlAttached,
                            "surfaceReady returned before SessionWindow.OnLoaded parented the control — parent-before-connect is broken.");
                    }
                    else
                    {
                        ShowEmbeddedSession(s, "Connecting to " + s.VmName);
                    }
                }, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                TearDownPendingSurface(standalone);
                System.Windows.MessageBox.Show(this, "Connection was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            catch (VmConnectionException ex)
            {
                TearDownPendingSurface(standalone);
                _ = _viewModel.RecordConnectionAsync(vm, success: false, ex.Message);
                System.Windows.MessageBox.Show(this, ex.Message, "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (Exception ex)
            {
                TearDownPendingSurface(standalone);
                _log.Error($"Unexpected error connecting to '{vm.Name}': {ex.Message}", ex);
                _ = _viewModel.RecordConnectionAsync(vm, success: false, ex.Message);
                System.Windows.MessageBox.Show(this, $"An unexpected error occurred:\n{ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                progressDialog.Close();
            }

            if (session.State != VMDesk.Core.Enums.ConnectionState.Connected)
            {
                TearDownPendingSurface(standalone);
                _ = _viewModel.RecordConnectionAsync(vm, success: false, session.LastError);
                System.Windows.MessageBox.Show(this,
                    session.LastError ?? "The connection attempt did not complete.",
                    "Connection Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _ = _viewModel.RecordConnectionAsync(vm, success: true, null);

            // The surface was opened in surfaceReady before the dial; only the
            // embedded status line still needs the final connected text. External
            // sessions live in the mstsc window, so their text points there.
            if (!separateWindow && _embeddedSession == session)
            {
                EmbeddedStatus.Text = session.Capabilities.ExternalClient
                    ? DescribeExternalSession(session)
                    : "Connected to " + session.VmName;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Could not start the connection to '{vm.Name}': {ex.Message}", ex);
            ShowError("Connection Error", ex);
        }
    }

    /// <summary>
    /// Routes an already-connected session to its launch mode: an independent
    /// session window when the VM prefers SeparateWindow, otherwise the embedded
    /// workspace. Only used by the re-activation path now — fresh connects surface
    /// through surfaceReady in ConnectAsync (parent-before-connect ordering).
    /// </summary>
    private void OpenSessionSurface(IRemoteSession session, VirtualMachineEntity vm)
    {
        if (SessionLaunchResolver.IsSeparateWindow(vm))
        {
            var standalone = new SessionWindow(session, _sessions, _transfer) { Owner = this };
            session.HostMode = VMDesk.Core.Enums.SessionHostMode.Standalone;
            standalone.Show();
        }
        else
        {
            ShowEmbeddedSession(session, "Connected to " + session.VmName);
        }
    }

    private void ShowEmbeddedSession(IRemoteSession session, string statusText)
    {
        _embeddedSession = session;
        LibrarySurface.Visibility = Visibility.Collapsed;
        EmbeddedSessionSurface.Visibility = Visibility.Visible;
        EmbeddedTitle.Text = session.VmName;
        if (session.HostControl is System.Windows.Forms.Control control)
        {
            ExternalSessionHint.Visibility = Visibility.Collapsed;
            _embeddedHost = new WindowsFormsHost { Child = control };
            EmbeddedHost.Child = _embeddedHost;
            session.HostMode = VMDesk.Core.Enums.SessionHostMode.Embedded;
            EmbeddedStatus.Text = statusText;
        }
        else if (session.Capabilities.ExternalClient)
        {
            // External mstsc fallback (Task 3): the session lives in its own
            // Windows Remote Desktop window. The embedded surface stays as the
            // control panel — Back returns to the library, Disconnect closes the
            // session (and the client window) — with the real session state
            // owned by mstsc itself.
            EmbeddedHost.Child = null;
            _embeddedHost = null;
            session.HostMode = VMDesk.Core.Enums.SessionHostMode.Standalone;
            ExternalSessionHint.Visibility = Visibility.Visible;
            ExternalSessionHint.Text = session is VMDesk.Rdp.VmConnectExternalSession
                ? "Session opened in Hyper-V Virtual Machine Connection"
                : "Session opened in Windows Remote Desktop";
            EmbeddedStatus.Text = statusText;
            BindExternalStatusText(session);
        }
        else
        {
            EmbeddedStatus.Text = "RDP control unavailable on this Windows installation.";
        }
    }

    /// <summary>
    /// The external client's state is not observable from the embedded surface, so the
    /// status line must mirror the session's StateChanged events instead of staying
    /// static text. Subscribed once per session; the handler no-ops for any session
    /// that is no longer the embedded one (a later task restyles this surface).
    /// </summary>
    private void BindExternalStatusText(IRemoteSession session)
    {
        if (_externalStatusSession == session)
        {
            return;
        }

        _externalStatusSession = session;
        session.StateChanged += (_, e) => Dispatcher.Invoke(() =>
        {
            if (_embeddedSession == session)
            {
                EmbeddedStatus.Text = $"Windows Remote Desktop session: {e.State}.";
            }
        });
    }

    /// <summary>
    /// Undoes the pre-connect surface opened by surfaceReady when the dial fails or
    /// is cancelled: closes the standalone window or returns to the library. The
    /// manager already closed an owned session for the cancel path; failed sessions
    /// stay in the workspace for visibility (spec §57), but their HostMode is reset
    /// to Closed once the control is unparented so it reflects reality.
    /// </summary>
    private void TearDownPendingSurface(SessionWindow? standalone)
    {
        if (standalone is not null)
        {
            standalone.Close();
            return;
        }

        if (_embeddedSession is null) return;
        var orphaned = _embeddedSession;
        _embeddedSession = null;
        EmbeddedHost.Child = null;
        _embeddedHost = null;
        ExternalSessionHint.Visibility = Visibility.Collapsed;
        orphaned.HostMode = VMDesk.Core.Enums.SessionHostMode.Closed;
        EmbeddedSessionSurface.Visibility = Visibility.Collapsed;
        LibrarySurface.Visibility = Visibility.Visible;
    }

    private void BackToLibraryClick(object sender, RoutedEventArgs e)
    {
        EmbeddedHost.Child = null;
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
        if (_embeddedSession is not null)
        {
            var session = _embeddedSession;
            _embeddedSession = null;
            BackToLibraryClick(sender, e);
            await _sessions.CloseAsync(session);
        }
    }

    private async void ScopeClick(object sender, RoutedEventArgs e) => await _viewModel.SetScopeAsync((string)((FrameworkElement)sender).Tag);

    private void CollapseSidebarClick(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = _sidebarCollapsed ? new GridLength(72) : new GridLength(260);
        SidebarHeaderContent.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
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

    private async void SettingsClick(object sender, RoutedEventArgs e)
    {
        new SettingsWindow((App)System.Windows.Application.Current, _viewModel.GetSettingsService()) { Owner = this }.ShowDialog();
        await _viewModel.ApplySavedLibraryViewAsync();
    }

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