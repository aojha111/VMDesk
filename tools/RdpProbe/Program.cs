using System.Reflection;
using System.Runtime.InteropServices;
using AxMSTSCLib;
using VMDesk.Rdp.Interop;

// STA so CoCreateInstance matches the conditions the WPF app runs under.
var t = new Thread(() =>
{
    var (probeAvailable, probeDetails) = RdpControlFactory.Probe();
    Console.WriteLine($"PROBE available={probeAvailable}");
    Console.WriteLine($"PROBE details={probeDetails}");
    try
    {
        var ctl = RdpControlFactory.CreateControl();
        Console.WriteLine($"CREATECONTROL ok coclass={ctl.CoclassName}");
        ctl.Control.Dispose();
    }
    catch (Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            Console.WriteLine($"CREATECONTROL {e.GetType().Name}: {e.Message}");
    }
    var names = new[]
    {
        "AxMSTSCLib.AxMsRdpClient12NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient11NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient10NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient9NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient8NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient7NotSafeForScripting",
    };
    foreach (var name in names)
    {
        var type = typeof(AxMsRdpClient9NotSafeForScripting).Assembly.GetType(name);
        if (type is null) { Console.WriteLine($"{name}: wrapper type missing"); continue; }
        Console.WriteLine($"{name}: attributes = [" + string.Join(", ",
            type.GetCustomAttributes(false).Select(a => a.GetType().FullName)) + "]");
        foreach (var a in type.GetCustomAttributes(false))
        {
            if (!a.GetType().Name.Contains("Clsid", StringComparison.OrdinalIgnoreCase)) continue;
            var at = a.GetType();
            Console.WriteLine($"  attr {at.FullName}: props = [" + string.Join(", ",
                at.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(p => p.Name + "=" + (p.GetValue(a) ?? "<null>"))) + "]"
                + $" fields = [" + string.Join(", ",
                    at.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name + "=" + (f.GetValue(a) ?? "<null>"))) + "]");
        }
    }

    // Is the DLL itself present and loadable?
    var path = Path.Combine(Environment.SystemDirectory, "mstscax.dll");
    Console.WriteLine($"mstscax.dll exists={File.Exists(path)}");
    if (File.Exists(path))
    {
        var h = LoadLibraryEx(path, IntPtr.Zero, 0x00000400); // LOAD_LIBRARY_AS_DATAFILE
        Console.WriteLine($"LoadLibraryEx(datafile) handle=0x{h:X} err={Marshal.GetLastWin32Error()}");
        var h2 = LoadLibrary(path);
        Console.WriteLine($"LoadLibrary handle=0x{h2:X} err={Marshal.GetLastWin32Error()}");
    }
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
return 0;

partial class Program
{
    internal const uint CLSCTX_INPROC_SERVER = 1;

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LoadLibrary(string lpFileName);
}
