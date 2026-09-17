namespace VMDesk.Rdp;

/// <summary>Connection parameters set by the engine factory before connect.</summary>
public sealed class PendingOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Password { get; set; }
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public int ColorDepth { get; set; } = 32;
    public bool SmartSizing { get; set; } = true;
    public bool FullScreen { get; set; }
    public bool UseAllMonitors { get; set; }
    public bool AdminSession { get; set; }
    public bool ClipboardRedirect { get; set; } = true;
    public bool AudioRedirect { get; set; } = true;
    public bool MicrophoneRedirect { get; set; }
    public bool DriveRedirect { get; set; }
    public string DriveList { get; set; } = "*";
    public bool PrinterRedirect { get; set; }
    public bool SmartCardRedirect { get; set; }
    public bool PortRedirect { get; set; }
    public string KeyboardHookMode { get; set; } = "OnTheFly";
    public bool ConnectionBar { get; set; } = true;
    public string PerformancePreset { get; set; } = "Automatic";
    public bool AutoReconnect { get; set; } = true;
    public bool UseGateway { get; set; }
    public string GatewayServer { get; set; } = string.Empty;
    public string GatewayUsername { get; set; } = string.Empty;
}
