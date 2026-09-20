using System.Text.Json;
using VMDesk.Core.Entities;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>Mapping between VM entities and import/export records. No password material involved.</summary>
public static class ImportMapper
{
    public static ImportExportVm ToExportVm(VirtualMachineEntity vm)
    {
        var settings = new VmImportSettings
        {
            ScreenWidth = vm.ScreenWidth,
            ScreenHeight = vm.ScreenHeight,
            ColorDepth = vm.ColorDepth,
            SmartSizing = vm.SmartSizing,
            FullScreen = vm.FullScreen,
            MultiMonitor = vm.MultiMonitor,
            UseAllMonitors = vm.UseAllMonitors,
            AlwaysOnTop = vm.AlwaysOnTop,
            ClipboardRedirection = vm.ClipboardRedirection,
            FileClipboardEnabled = vm.FileClipboardEnabled,
            AudioRedirection = vm.AudioRedirection,
            MicrophoneRedirection = vm.MicrophoneRedirection,
            DriveRedirection = vm.DriveRedirection,
            DrivesToRedirect = vm.DrivesToRedirect,
            PrinterRedirection = vm.PrinterRedirection,
            SmartCardRedirection = vm.SmartCardRedirection,
            PortRedirection = vm.PortRedirection,
            KeyboardHookMode = vm.KeyboardHookMode,
            ConnectionBarEnabled = vm.ConnectionBarEnabled,
            PerformancePreset = vm.PerformancePreset,
            ReconnectAutomatically = vm.ReconnectAutomatically,
            AdminSession = vm.AdminSession,
            UseGatewayServer = vm.UseGatewayServer,
            GatewayServer = vm.GatewayServer,
            GatewayUsername = vm.GatewayUsername,
            PreferredSessionDisplayMode = vm.PreferredSessionDisplayMode,
            ConnectionTimeoutSeconds = vm.ConnectionTimeoutSeconds,
            ConnectionRetryCount = vm.ConnectionRetryCount,
            ConnectionRetryDelaySeconds = vm.ConnectionRetryDelaySeconds,
            ReconnectTimeoutSeconds = vm.ReconnectTimeoutSeconds,
            Notes = vm.Notes
        };

        return new ImportExportVm
        {
            Id = vm.Id,
            Name = vm.Name,
            Description = vm.Description,
            Host = vm.Host,
            Port = vm.Port,
            Protocol = vm.Protocol,
            Username = vm.Username,
            Domain = vm.Domain,
            OperatingSystem = vm.OperatingSystem,
            Group = vm.Group?.Name,
            Tags = vm.Tags.Select(t => t.Tag?.Name ?? string.Empty).Where(t => t.Length > 0).ToList(),
            Favorite = vm.Favorite,
            Color = vm.Color,
            Icon = vm.Icon,
            SettingsJson = JsonSerializer.Serialize(settings, ImportExportService.JsonOptions)
        };
    }

    public static void ApplyExport(VirtualMachineEntity target, ImportExportVm export)
    {
        target.Name = export.Name;
        target.Description = export.Description;
        target.Host = export.Host;
        target.Port = export.Port; // 0 = default RDP port (3389).
        target.Protocol = string.IsNullOrWhiteSpace(export.Protocol) ? "Rdp" : export.Protocol;
        target.Username = export.Username;
        target.Domain = export.Domain;
        target.OperatingSystem = export.OperatingSystem;
        target.Favorite = export.Favorite;
        target.Color = export.Color;
        target.Icon = string.IsNullOrWhiteSpace(export.Icon) ? "Desktop" : export.Icon;

        VmImportSettings? settings = null;
        try
        {
            settings = JsonSerializer.Deserialize<VmImportSettings>(export.SettingsJson, ImportExportService.JsonOptions);
        }
        catch (JsonException)
        {
            // Fall back to defaults for malformed settings.
        }

        if (settings is not null)
        {
            ApplySettings(target, settings);
        }
    }

    private static void ApplySettings(VirtualMachineEntity vm, VmImportSettings s)
    {
        vm.ScreenWidth = s.ScreenWidth;
        vm.ScreenHeight = s.ScreenHeight;
        vm.ColorDepth = s.ColorDepth;
        vm.SmartSizing = s.SmartSizing;
        vm.FullScreen = s.FullScreen;
        vm.MultiMonitor = s.MultiMonitor;
        vm.UseAllMonitors = s.UseAllMonitors;
        vm.AlwaysOnTop = s.AlwaysOnTop;
        vm.ClipboardRedirection = s.ClipboardRedirection;
        vm.FileClipboardEnabled = s.FileClipboardEnabled;
        vm.AudioRedirection = s.AudioRedirection;
        vm.MicrophoneRedirection = s.MicrophoneRedirection;
        vm.DriveRedirection = s.DriveRedirection;
        vm.DrivesToRedirect = s.DrivesToRedirect;
        vm.PrinterRedirection = s.PrinterRedirection;
        vm.SmartCardRedirection = s.SmartCardRedirection;
        vm.PortRedirection = s.PortRedirection;
        vm.KeyboardHookMode = s.KeyboardHookMode;
        vm.ConnectionBarEnabled = s.ConnectionBarEnabled;
        vm.PerformancePreset = s.PerformancePreset;
        vm.ReconnectAutomatically = s.ReconnectAutomatically;
        vm.AdminSession = s.AdminSession;
        vm.UseGatewayServer = s.UseGatewayServer;
        vm.GatewayServer = s.GatewayServer;
        vm.GatewayUsername = s.GatewayUsername;
        vm.PreferredSessionDisplayMode = s.PreferredSessionDisplayMode;
        vm.ConnectionTimeoutSeconds = Math.Clamp(s.ConnectionTimeoutSeconds, 5, 600);
        vm.ConnectionRetryCount = Math.Max(0, s.ConnectionRetryCount);
        vm.ConnectionRetryDelaySeconds = Math.Max(0, s.ConnectionRetryDelaySeconds);
        vm.ReconnectTimeoutSeconds = Math.Clamp(s.ReconnectTimeoutSeconds, 5, 600);
        vm.Notes = s.Notes;
    }
}
