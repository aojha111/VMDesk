using VMDesk.Core.Entities;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>Search, filter, duplicate helpers + credential reference generation (spec §19, §62, §7).</summary>
public static class VmFilter
{
    /// <summary>Deterministic credential target derived from the VM GUID.</summary>
    public static string CredentialReferenceFor(Guid vmId) => CredentialNaming.For(vmId);

    /// <summary>Debounced in-memory search across name/host/IP/user/description/group/tags/notes.</summary>
    public static IReadOnlyList<VirtualMachineEntity> Apply(
        IEnumerable<VirtualMachineEntity> source,
        string? search,
        string? groupName,
        IReadOnlyList<string>? requiredTags,
        bool favoritesOnly,
        Func<VirtualMachineEntity, bool>? statusPredicate)
    {
        var result = new List<VirtualMachineEntity>();
        var terms = SplitTerms(search);

        foreach (var vm in source)
        {
            if (favoritesOnly && !vm.Favorite)
            {
                continue;
            }

            if (groupName is not null && !string.Equals(vm.Group?.Name, groupName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (requiredTags is { Count: > 0 })
            {
                var vmTags = vm.Tags.Select(t => t.Tag?.Name ?? string.Empty).ToList();
                if (requiredTags.Any(t => !vmTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (statusPredicate is not null && !statusPredicate(vm))
            {
                continue;
            }

            if (terms.Length == 0)
            {
                result.Add(vm);
                continue;
            }

            var haystack = string.Join(' ',
                vm.Name, vm.Host, vm.Username, vm.Description, vm.Group?.Name ?? string.Empty,
                vm.Notes, string.Join(' ', vm.Tags.Select(t => t.Tag?.Name ?? string.Empty)));

            var matched = true;
            foreach (var term in terms)
            {
                if (!haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                result.Add(vm);
            }
        }

        return result;
    }

    public static VirtualMachineEntity CloneForDuplicate(VirtualMachineEntity source)
    {
        return new VirtualMachineEntity
        {
            Name = $"{source.Name} (copy)",
            Description = source.Description,
            Host = source.Host,
            Port = source.Port,
            Protocol = source.Protocol,
            Username = source.Username,
            Domain = source.Domain,
            OperatingSystem = source.OperatingSystem,
            Notes = source.Notes,
            Favorite = source.Favorite,
            Color = source.Color,
            Icon = source.Icon,
            ScreenWidth = source.ScreenWidth,
            ScreenHeight = source.ScreenHeight,
            ColorDepth = source.ColorDepth,
            SmartSizing = source.SmartSizing,
            FullScreen = source.FullScreen,
            MultiMonitor = source.MultiMonitor,
            UseAllMonitors = source.UseAllMonitors,
            AlwaysOnTop = source.AlwaysOnTop,
            ClipboardRedirection = source.ClipboardRedirection,
            FileClipboardEnabled = source.FileClipboardEnabled,
            AudioRedirection = source.AudioRedirection,
            MicrophoneRedirection = source.MicrophoneRedirection,
            DriveRedirection = source.DriveRedirection,
            DrivesToRedirect = source.DrivesToRedirect,
            PrinterRedirection = source.PrinterRedirection,
            SmartCardRedirection = source.SmartCardRedirection,
            PortRedirection = source.PortRedirection,
            KeyboardHookMode = source.KeyboardHookMode,
            ConnectionBarEnabled = source.ConnectionBarEnabled,
            PerformancePreset = source.PerformancePreset,
            ReconnectAutomatically = source.ReconnectAutomatically,
            AdminSession = source.AdminSession,
            UseGatewayServer = source.UseGatewayServer,
            GatewayServer = source.GatewayServer,
            GatewayUsername = source.GatewayUsername,
            PreferredSessionDisplayMode = source.PreferredSessionDisplayMode,
            ConnectionTimeoutSeconds = source.ConnectionTimeoutSeconds,
            ConnectionRetryCount = source.ConnectionRetryCount,
            ConnectionRetryDelaySeconds = source.ConnectionRetryDelaySeconds,
            ReconnectTimeoutSeconds = source.ReconnectTimeoutSeconds
        };
    }

    private static string[] SplitTerms(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return Array.Empty<string>();
        }

        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
