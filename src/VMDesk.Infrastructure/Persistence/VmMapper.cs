using VMDesk.Core.Entities;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>Field copy helpers between detached VM entities and tracked EF entities.</summary>
public static class VmMapper
{
    public static void CopyFields(VirtualMachineEntity source, VirtualMachineEntity target)
    {
        target.Name = source.Name;
        target.Description = source.Description;
        target.Host = source.Host;
        target.Port = source.Port;
        target.Protocol = source.Protocol;
        target.Username = source.Username;
        target.Domain = source.Domain;
        target.CredentialReference = source.CredentialReference;
        target.OperatingSystem = source.OperatingSystem;
        target.Provider = source.Provider;
        target.ProviderId = source.ProviderId;
        target.PowerState = source.PowerState;
        target.GroupId = source.GroupId;
        target.Notes = source.Notes;
        target.Favorite = source.Favorite;
        target.Color = source.Color;
        target.Icon = source.Icon;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        target.LastConnectedAt = source.LastConnectedAt;
        target.LastConnectionStatus = source.LastConnectionStatus;
        target.LastConnectionError = source.LastConnectionError;
        target.LastKnownLatencyMs = source.LastKnownLatencyMs;
        target.ConnectionProfileId = source.ConnectionProfileId;
        target.ScreenWidth = source.ScreenWidth;
        target.ScreenHeight = source.ScreenHeight;
        target.ColorDepth = source.ColorDepth;
        target.SmartSizing = source.SmartSizing;
        target.FullScreen = source.FullScreen;
        target.MultiMonitor = source.MultiMonitor;
        target.UseAllMonitors = source.UseAllMonitors;
        target.AlwaysOnTop = source.AlwaysOnTop;
        target.WindowLeft = source.WindowLeft;
        target.WindowTop = source.WindowTop;
        target.WindowWidth = source.WindowWidth;
        target.WindowHeight = source.WindowHeight;
        target.WindowMaximized = source.WindowMaximized;
        target.ClipboardRedirection = source.ClipboardRedirection;
        target.FileClipboardEnabled = source.FileClipboardEnabled;
        target.AudioRedirection = source.AudioRedirection;
        target.MicrophoneRedirection = source.MicrophoneRedirection;
        target.DriveRedirection = source.DriveRedirection;
        target.DrivesToRedirect = source.DrivesToRedirect;
        target.PrinterRedirection = source.PrinterRedirection;
        target.SmartCardRedirection = source.SmartCardRedirection;
        target.PortRedirection = source.PortRedirection;
        target.KeyboardHookMode = source.KeyboardHookMode;
        target.ConnectionBarEnabled = source.ConnectionBarEnabled;
        target.PerformancePreset = source.PerformancePreset;
        target.ReconnectAutomatically = source.ReconnectAutomatically;
        target.AdminSession = source.AdminSession;
        target.UseGatewayServer = source.UseGatewayServer;
        target.GatewayServer = source.GatewayServer;
        target.GatewayUsername = source.GatewayUsername;
        target.PreferredSessionDisplayMode = source.PreferredSessionDisplayMode;
        target.ConnectionTimeoutSeconds = source.ConnectionTimeoutSeconds;
        target.ConnectionRetryCount = source.ConnectionRetryCount;
        target.ConnectionRetryDelaySeconds = source.ConnectionRetryDelaySeconds;
        target.ReconnectTimeoutSeconds = source.ReconnectTimeoutSeconds;
    }
}
