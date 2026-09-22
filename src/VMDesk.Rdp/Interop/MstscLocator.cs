namespace VMDesk.Rdp.Interop;

/// <summary>
/// Locates the built-in Windows Remote Desktop client (mstsc.exe) used as the
/// external-session fallback when the ActiveX control cannot be instantiated.
/// </summary>
public static class MstscLocator
{
    /// <summary>Full path to %SystemRoot%\System32\mstsc.exe, or null when it does not exist.</summary>
    public static string? TryGetFullPath()
    {
        try
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var path = Path.Combine(system32, "mstsc.exe");
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
