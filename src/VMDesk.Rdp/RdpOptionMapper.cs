using System.Text.Json;
using VMDesk.Core.Entities;
using VMDesk.Core.Models;

namespace VMDesk.Rdp;

/// <summary>Maps a VM entity (plus resolved credential) to engine options. Pure data (spec §9).</summary>
public static class RdpOptionMapper
{
    public static RdpConnectionOptions FromVm(VirtualMachineEntity vm, string? password)
    {
        var user = vm.Username ?? string.Empty;
        var domain = vm.Domain ?? string.Empty;
        if (user.Contains('\\', StringComparison.Ordinal) && domain.Length == 0)
        {
            var slash = user.IndexOf('\\');
            domain = user[..slash];
            user = user[(slash + 1)..];
        }

        return new RdpConnectionOptions(
            Host: vm.Host,
            Port: vm.Port,
            Username: user,
            Domain: domain,
            Password: password,
            ScreenWidth: Math.Clamp(vm.ScreenWidth, 640, 8192),
            ScreenHeight: Math.Clamp(vm.ScreenHeight, 480, 8192),
            ColorDepth: vm.ColorDepth,
            SmartSizing: vm.SmartSizing,
            FullScreen: vm.FullScreen,
            MultiMonitor: vm.MultiMonitor,
            UseAllMonitors: vm.UseAllMonitors,
            AdminSession: vm.AdminSession,
            ClipboardRedirect: vm.ClipboardRedirection,
            FileClipboard: vm.FileClipboardEnabled,
            AudioRedirect: vm.AudioRedirection,
            MicrophoneRedirect: vm.MicrophoneRedirection,
            DriveRedirect: vm.DriveRedirection,
            DrivesToRedirect: string.IsNullOrWhiteSpace(vm.DrivesToRedirect) ? "*" : vm.DrivesToRedirect,
            PrinterRedirect: vm.PrinterRedirection,
            SmartCardRedirect: vm.SmartCardRedirection,
            PortRedirect: vm.PortRedirection,
            KeyboardHookMode: vm.KeyboardHookMode,
            ConnectionBar: vm.ConnectionBarEnabled,
            PerformancePreset: vm.PerformancePreset,
            ReconnectAutomatically: vm.ReconnectAutomatically,
            UseGateway: vm.UseGatewayServer,
            GatewayServer: vm.GatewayServer,
            GatewayUsername: vm.GatewayUsername);
    }
}

/// <summary>Performance preset → RDP PerformanceFlags mapping (spec §9).</summary>
public static class PerformanceFlagsMapper
{
    public static uint Map(string preset)
    {
        // WM PerformanceFlags: 1 disable wallpaper, 2 disable full-window drag,
        // 4 disable menu animations, 8 disable themes, 16 disable cursor settings,
        // 32 disable font smoothing, 64 enable bitmap caching? (bitmap persistence separate),
        // 128 disable show window contents while dragging, 256 disable desktop composition.
        return preset switch
        {
            "Lan" => 0,                 // full experience
            "Broadband" => 0,
            "HighLatency" => 1 | 2 | 4 | 32 | 256,   // modest look, keep fonts cached
            "LowBandwidth" => 1 | 2 | 4 | 8 | 16 | 32 | 128 | 256,
            _ => 0                      // Automatic → client negotiates
        };
    }
}
