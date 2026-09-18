using VMDesk.Core.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace VMDesk.Core.Entities;

/// <summary>A saved remote VM connection (spec §6, §82, §98).</summary>
public class VirtualMachineEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public string Protocol { get; set; } = ProtocolKind.Rdp.ToString();
    public string Username { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string CredentialReference { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public Guid? GroupId { get; set; }
    public GroupEntity? Group { get; set; }
    public List<VmTagEntity> Tags { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public string Color { get; set; } = string.Empty;
    public string Icon { get; set; } = "Desktop";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastConnectedAt { get; set; }
    public string LastConnectionStatus { get; set; } = ConnectionState.Disconnected.ToString();
    public string LastConnectionError { get; set; } = string.Empty;
    public int? LastKnownLatencyMs { get; set; }

    /// <summary>
    /// Display state derived from the persisted last connection status (spec §16). Tiles and list
    /// rows bind to State, so the card overlay, status dot, and status text reflect the stored state.
    /// </summary>
    [NotMapped]
    public ConnectionState State => Enum.TryParse(LastConnectionStatus, ignoreCase: true, out ConnectionState state)
        ? state
        : ConnectionState.Disconnected;

    public Guid? ConnectionProfileId { get; set; }
    public ConnectionProfileEntity? ConnectionProfile { get; set; }

    // Display
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public int ColorDepth { get; set; } = 32;
    public bool SmartSizing { get; set; } = true;
    public bool FullScreen { get; set; }
    public bool MultiMonitor { get; set; }
    public bool UseAllMonitors { get; set; }
    public bool AlwaysOnTop { get; set; }

    // Window placement for standalone mode (spec §78)
    public int WindowLeft { get; set; } = int.MinValue;
    public int WindowTop { get; set; } = int.MinValue;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    // Redirection profile (spec §98)
    public bool ClipboardRedirection { get; set; } = true;
    public bool FileClipboardEnabled { get; set; } = true;
    public bool AudioRedirection { get; set; } = true;
    public bool MicrophoneRedirection { get; set; }
    public bool DriveRedirection { get; set; }
    public string DrivesToRedirect { get; set; } = "*";
    public bool PrinterRedirection { get; set; }
    public bool SmartCardRedirection { get; set; }
    public bool PortRedirection { get; set; }

    // Advanced
    public string KeyboardHookMode { get; set; } = global::VMDesk.Core.Enums.KeyboardHookMode.OnTheFly.ToString();
    public bool ConnectionBarEnabled { get; set; } = true;
    public string PerformancePreset { get; set; } = global::VMDesk.Core.Enums.PerformancePreset.Automatic.ToString();
    public bool ReconnectAutomatically { get; set; } = true;
    public bool AdminSession { get; set; }
    public bool UseGatewayServer { get; set; }
    public string GatewayServer { get; set; } = string.Empty;
    public string GatewayUsername { get; set; } = string.Empty;

    // Session display mode (spec §77)
    public string PreferredSessionDisplayMode { get; set; } = SessionDisplayMode.Embedded.ToString();

    // Per-VM connection timeouts (spec §82)
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int ConnectionRetryCount { get; set; } = 1;
    public int ConnectionRetryDelaySeconds { get; set; } = 5;
    public int ReconnectTimeoutSeconds { get; set; } = 30;
}

public class GroupEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<VirtualMachineEntity> VirtualMachines { get; set; } = new();
}

public class TagEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<VmTagEntity> Vms { get; set; } = new();
}

public class VmTagEntity
{
    public Guid VmId { get; set; }
    public VirtualMachineEntity? Vm { get; set; }
    public Guid TagId { get; set; }
    public TagEntity? Tag { get; set; }
}

public class ConnectionProfileEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string OptionsJson { get; set; } = "{}";
}

public class ApplicationSettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class RecentConnectionEntity
{
    public long Id { get; set; }
    public Guid VmId { get; set; }
    public string VmName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public DateTimeOffset ConnectedAt { get; set; }
    public bool Success { get; set; }
}
