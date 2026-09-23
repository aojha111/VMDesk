namespace VMDesk.Rdp.Interop;

/// <summary>
/// Locates vmconnect.exe, the Hyper-V Virtual Machine Connection console launcher used for
/// discovered Hyper-V VMs that have no RDP address. Not present unless the Hyper-V management
/// tools are installed, so the probe must fail quietly (null) rather than throw.
/// </summary>
public static class VmConnectLocator
{
    /// <summary>Full path to %SystemRoot%\System32\vmconnect.exe, or null when it does not exist.</summary>
    public static string? TryGetFullPath() => TryGetFullPath(File.Exists);

    /// <summary>
    /// Probe with an injectable existence check so tests never depend on whether this machine
    /// has the Hyper-V management tools. Returns the System32 path only when the check says yes.
    /// </summary>
    public static string? TryGetFullPath(Func<string, bool> fileExists)
    {
        try
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var path = Path.Combine(system32, "vmconnect.exe");
            return fileExists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
