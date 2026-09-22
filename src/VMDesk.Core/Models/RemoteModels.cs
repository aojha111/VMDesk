namespace VMDesk.Core.Models;

public sealed record DiagnosticsInfo(
    string AppVersion,
    string OsVersion,
    string RuntimeVersion,
    string RdpControlStatus,
    string DatabasePath,
    string DatabaseStatus,
    string CredentialStoreStatus,
    string NetworkAdapters,
    string LogPath);

/// <summary>Connection options resolved for an engine; pure data, no COM (spec §9).</summary>
public sealed record RdpConnectionOptions(
    string Host,
    int Port,
    string Username,
    string Domain,
    string? Password,
    int ScreenWidth,
    int ScreenHeight,
    int ColorDepth,
    bool SmartSizing,
    bool FullScreen,
    bool MultiMonitor,
    bool UseAllMonitors,
    bool AdminSession,
    bool ClipboardRedirect,
    bool FileClipboard,
    bool AudioRedirect,
    bool MicrophoneRedirect,
    bool DriveRedirect,
    string DrivesToRedirect,
    bool PrinterRedirect,
    bool SmartCardRedirect,
    bool PortRedirect,
    string KeyboardHookMode,
    bool ConnectionBar,
    string PerformancePreset,
    bool ReconnectAutomatically,
    bool UseGateway,
    string GatewayServer,
    string GatewayUsername);

public sealed record SessionCapabilities
{
    public bool ClipboardSupported { get; init; } = true;
    public bool FileClipboardSupported { get; init; } = true;
    public bool DriveRedirectionSupported { get; init; } = true;
    public bool SmartSizingSupported { get; init; } = true;
    public bool MultiMonitorSupported { get; init; } = true;
    public bool AudioRedirectionSupported { get; init; } = true;
    public bool PrinterRedirectionSupported { get; init; } = true;
    public bool GatewaySupported { get; init; } = true;
    public bool CtrlAltDelSupported { get; init; } = true;
    /// <summary>True when the session runs in an external client process (mstsc.exe) instead of the embedded control.</summary>
    public bool ExternalClient { get; init; } = false;
}

/// <summary>Serialised VM display/connection settings inside import files.</summary>
public sealed record VmImportSettings
{
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public int ColorDepth { get; set; } = 32;
    public bool SmartSizing { get; set; } = true;
    public bool FullScreen { get; set; }
    public bool MultiMonitor { get; set; }
    public bool UseAllMonitors { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool ClipboardRedirection { get; set; } = true;
    public bool FileClipboardEnabled { get; set; } = true;
    public bool AudioRedirection { get; set; } = true;
    public bool MicrophoneRedirection { get; set; }
    public bool DriveRedirection { get; set; }
    public string DrivesToRedirect { get; set; } = "*";
    public bool PrinterRedirection { get; set; }
    public bool SmartCardRedirection { get; set; }
    public bool PortRedirection { get; set; }
    public string KeyboardHookMode { get; set; } = "OnTheFly";
    public bool ConnectionBarEnabled { get; set; } = true;
    public string PerformancePreset { get; set; } = "Automatic";
    public bool ReconnectAutomatically { get; set; } = true;
    public bool AdminSession { get; set; }
    public bool UseGatewayServer { get; set; }
    public string GatewayServer { get; set; } = string.Empty;
    public string GatewayUsername { get; set; } = string.Empty;
    public string PreferredSessionDisplayMode { get; set; } = "Embedded";
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int ConnectionRetryCount { get; set; } = 1;
    public int ConnectionRetryDelaySeconds { get; set; } = 5;
    public int ReconnectTimeoutSeconds { get; set; } = 30;
    public string Notes { get; set; } = string.Empty;
}
