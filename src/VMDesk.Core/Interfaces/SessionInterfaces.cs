using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Models;

namespace VMDesk.Core.Interfaces;

/// <summary>Export/import of VM metadata; never contains passwords (spec §25).</summary>
public interface IImportExportService
{
    Task ExportAsync(string filePath, IReadOnlyList<VirtualMachineEntity> vms);
    Task<ImportExportFile> ReadAsync(string filePath);
    Task<ImportResult> ImportAsync(string filePath, ImportConflictStrategy strategy);
}

/// <summary>Timestamped database backup/restore with validation (spec §26).</summary>
public interface IBackupService
{
    Task<string> BackupAsync(string? destinationDirectory = null);
    Task RestoreAsync(string backupPath);
    Task<bool> ValidateAsync(string databasePath);
}

/// <summary>Lightweight reachability test — DNS + TCP (spec §23).</summary>
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(string host, int port, TimeSpan timeout);
    Task<VmOnlineState> ProbeOnlineAsync(string host, int port, TimeSpan timeout);
}

/// <summary>Remote session abstraction used by the workspace. Implemented by the RDP engine.</summary>
public interface IRemoteSession : IAsyncDisposable
{
    Guid VmId { get; }
    string VmName { get; }
    ConnectionState State { get; }
    SessionHostMode HostMode { get; set; }
    /// <summary>WPF visual that renders the session (a WindowsFormsHost). Null until connected UI exists.</summary>
    object? HostControl { get; }
    SessionCapabilities Capabilities { get; }
    string? LastError { get; }
    string? TechnicalError { get; }

    event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    event EventHandler<SessionErrorEventArgs>? SessionError;

    /// <summary>
    /// Creates and configures the hosting control without connecting, so the caller can
    /// parent <see cref="HostControl"/> into a visible surface before <see cref="ConnectAsync"/>.
    /// No-op-safe on engines with no pre-connect control.
    /// </summary>
    void PrepareControl();

    /// <summary>
    /// Starts the connect on an already-prepared, already-parented control. No-op-safe on
    /// engines that only dial via <see cref="ConnectAsync"/>.
    /// </summary>
    void StartConnect();

    Task ConnectAsync(CancellationToken cancellationToken);
    Task ReconnectAsync();
    Task DisconnectAsync();
    void SendCtrlAltDel();
    void SetDisplayMode(DisplayScaleMode mode);
    void SetSmartSizing(bool enabled);
    void ToggleFullscreen();
    void SetClipboardRedirect(bool enabled);
    void SetAudioRedirect(bool enabled);
    void SetDriveRedirection(bool enabled, string drives);
    void Activate();

    /// <summary>Files placed on the remote clipboard by the user, when the platform exposes them; otherwise empty.</summary>
    IReadOnlyList<string> GetRemoteClipboardFiles();
}

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public SessionStateChangedEventArgs(ConnectionState state, string? message = null)
    {
        State = state;
        Message = message;
    }

    public ConnectionState State { get; }
    public string? Message { get; }
}

public sealed class SessionErrorEventArgs : EventArgs
{
    public SessionErrorEventArgs(string friendlyMessage, string technicalDetails)
    {
        FriendlyMessage = friendlyMessage;
        TechnicalDetails = technicalDetails;
    }

    public string FriendlyMessage { get; }
    public string TechnicalDetails { get; }
}

/// <summary>Creates remote sessions from VM entities. One implementation per engine (spec §1, §42).</summary>
public interface IRemoteSessionEngine
{
    string Name { get; }
    Task<IRemoteSession> CreateSessionAsync(VirtualMachineEntity vm, string? credentialReference = null);
    /// <summary>True when the Microsoft RDP ActiveX control is registered and loadable.</summary>
    Task<RdpAvailabilityInfo> CheckAvailabilityAsync();
}

/// <summary>Connection timeout / retry orchestration (spec §82-84).</summary>
public interface IConnectionOrchestrator
{
    Task ConnectAsync(IRemoteSession session, VirtualMachineEntity vm, IProgress<string>? progress, CancellationToken cancellationToken);
}

/// <summary>Workspace tiling layout engine. Knows nothing about RDP (spec §44).</summary>
public interface IWorkspaceLayoutService
{
    IReadOnlyList<LayoutRect> ComputeLayout(int sessionCount, int availableWidth, int availableHeight, WorkspaceTilingMode mode);
}

/// <summary>File transfer using RDP clipboard and redirected drives (spec §85-91, §96).</summary>
public interface IFileTransferService
{
    bool IsDriveTransferAvailable(IRemoteSession session);
    Task UploadAsync(IRemoteSession session, IReadOnlyList<string> localPaths, string remoteRoot, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);
    Task DownloadAsync(IRemoteSession session, IReadOnlyList<string> remotePaths, string localDirectory, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);
    /// <summary>Copies local files to the OS clipboard as a file list so the user can paste inside the remote session.</summary>
    void CopyFilesToClipboard(IReadOnlyList<string> localPaths);
    /// <summary>Returns the file list currently on the clipboard, for pasting to a local folder.</summary>
    IReadOnlyList<string> GetClipboardFiles();
}

/// <summary>Update check abstraction; no server in v1 (spec §55).</summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync();
}

/// <summary>Application diagnostics collection (spec §56).</summary>
public interface IDiagnosticsService
{
    Task<DiagnosticsInfo> CollectAsync();
    Task<string> TestDatabaseAsync();
    Task<string> TestCredentialStoreAsync();
}
