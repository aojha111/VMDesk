using VMDesk.Core.Entities;
using VMDesk.Core.Enums;

namespace VMDesk.Application.Services;

/// <summary>
/// Resolves how a VM is launched (spec §77): inside the main window's embedded
/// workspace or as an independent top-level session window. Pure decision logic,
/// unit-testable without WPF.
/// </summary>
public static class SessionLaunchResolver
{
    public const string Embedded = "Embedded";
    public const string SeparateWindow = "SeparateWindow";

    /// <summary>True when the VM should open as an independent session window.</summary>
    public static bool IsSeparateWindow(VirtualMachineEntity vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        return string.Equals(
            vm.PreferredSessionDisplayMode?.Trim(),
            SessionDisplayMode.SeparateWindow.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the VM should host the session inside the main window.</summary>
    public static bool IsEmbedded(VirtualMachineEntity vm) => !IsSeparateWindow(vm);

    /// <summary>Persists the launch preference for a VM (Embedded or SeparateWindow).</summary>
    public static void Apply(VirtualMachineEntity vm, bool separateWindow)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.PreferredSessionDisplayMode = separateWindow
            ? SessionDisplayMode.SeparateWindow.ToString()
            : SessionDisplayMode.Embedded.ToString();
    }
}
