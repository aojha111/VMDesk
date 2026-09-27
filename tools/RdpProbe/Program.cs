using System.Reflection;
using System.Runtime.InteropServices;
using AxMSTSCLib;
using MSTSCLib;
using VMDesk.Rdp.Interop;

if (args.Contains("dial"))
{
    var dt = new Thread(() => DialTest(args.Contains("orphan")));
    dt.SetApartmentState(ApartmentState.STA);
    dt.Start();
    dt.Join();
    return 0;
}

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

    static void DialTest(bool orphan)
    {
        // "orphan" mode reproduces the old factory order: CreateControl() with no
        // parent first, then SetParent into a host panel — vs "hosted" where the
        // handle is created after parenting.
        Console.WriteLine($"MODE orphan={orphan}");
        var rdp = new AxMsRdpClient11NotSafeForScripting();
        System.Windows.Forms.AxHost ax = rdp;
        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-2000, -2000),
            Size = new System.Drawing.Size(1024, 768),
            ShowInTaskbar = false,
        };
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        if (orphan) ax.CreateControl(); // pre-parent handle, like the old factory did
        panel.Controls.Add(ax);
        ax.Dock = DockStyle.Fill;
        rdp.OnDisconnected += (_, e) => Console.WriteLine($"EVENT OnDisconnected reason=0x{e.discReason:X8}");
        rdp.OnLogonError += (_, e) => Console.WriteLine("EVENT OnLogonError " + DumpProps(e));
        rdp.OnLoginComplete += (_, _) => Console.WriteLine("EVENT OnLoginComplete");
        rdp.OnWarning += (_, e) => Console.WriteLine("EVENT OnWarning " + DumpProps(e));
        form.Load += (_, _) =>
        {
            Console.WriteLine($"pre-connect handle={ax.IsHandleCreated} visible={ax.Visible} bounds={ax.Bounds}");
            rdp.Server = "127.0.0.1";
            rdp.UserName = "e2e-test";
            if (rdp.GetOcx() is IMsRdpClientNonScriptable5 ocx)
            {
                ocx.ClearTextPassword = "e2e-dummy-password";
                ocx.PromptForCredentials = false;
                ocx.EnableCredSspSupport = true;
            }
            var adv = (IMsRdpClientAdvancedSettings8)rdp.AdvancedSettings8;
            adv.RDPPort = 3389;
            adv.overallConnectionTimeout = 20;
            adv.singleConnectionTimeout = 20;
            try
            {
                rdp.Connect();
                Console.WriteLine("CONNECT() returned without throwing");
            }
            catch (Exception ex)
            {
                Console.WriteLine("CONNECT threw: " + ex);
            }
        };
        var timer = new System.Windows.Forms.Timer { Interval = 25000 };
        timer.Tick += (_, _) => { timer.Stop(); form.Close(); };
        timer.Start();
        Application.Run(form);
        Console.WriteLine("DIAL TEST DONE");
    }

    static string DumpProps(object e) => string.Join(", ", e.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => p.Name + "=" + (p.GetValue(e) ?? "<null>")));

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LoadLibrary(string lpFileName);
}
