using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using AxMSTSCLib;

namespace VMDesk.Rdp.Interop;

/// <summary>A created ActiveX control plus its concrete wrapper type (for its event multicaster).</summary>
public sealed record RdpControl(AxHost Control, Type ControlType, string CoclassName);

/// <summary>
/// Creates the Microsoft RDP ActiveX WinForms wrapper (spec §9, §66).
/// Uses the AxHost wrappers generated from the system mstscax.dll type library.
/// Tries the newest coclass first and falls back across Windows versions.
/// </summary>
public static class RdpControlFactory
{
    private static readonly string[] WrapperTypeNames =
    {
        "AxMSTSCLib.AxMsRdpClient12NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient11NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient10NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient9NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient8NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient7NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClient6NotSafeForScripting",
        "AxMSTSCLib.AxMsRdpClientNotSafeForScripting"
    };

    /// <summary>
    /// Creates the Ax control on the calling (STA UI) thread. Throws when unavailable.
    /// Each candidate is validated by real COM instantiation first: the AxHost ctor
    /// is lazy, so an unregistered coclass only explodes later at handle creation
    /// (CLASS_E_CLASSNOTAVAILABLE). The HWND must NOT be created here either — an
    /// AxHost whose handle was made while unparented never recreates it after the
    /// host (WindowsFormsHost) reparents it, and Connect() then silently no-ops.
    /// </summary>
    public static RdpControl CreateControl()
    {
        var failures = new List<string>();
        foreach (var name in WrapperTypeNames)
        {
            var type = ResolveWrapperType(name);
            if (type is null)
            {
                continue;
            }

            if (!TryGetClsid(type, out var clsid))
            {
                failures.Add(name + ": no CLSID attribute");
                continue;
            }

            if (!TryInstantiate(clsid))
            {
                failures.Add(name + ": CoCreateInstance failed");
                continue;
            }

            try
            {
                var control = (AxHost)Activator.CreateInstance(type)!;
                control.Name = "rdpClient";
                return new RdpControl(control, type, type.Name);
            }
            catch (COMException ex)
            {
                failures.Add(name + ": " + ex.Message);
            }
            catch (MissingMethodException ex)
            {
                failures.Add(name + ": " + ex.Message);
            }
        }

        throw new InvalidOperationException(
            "The Microsoft Remote Desktop ActiveX control (mstscax.dll) is not available. "
            + (failures.Count > 0 ? string.Join(" | ", failures) : "no candidate succeeded"));
    }

    /// <summary>
    /// Availability probe used by diagnostics (spec §56). Registry-free: a coclass
    /// counts as available only when CoCreateInstance actually creates it, so Probe
    /// can never claim an availability that CreateControl cannot deliver. Registry
    /// state is reported as detail text only.
    /// </summary>
    public static (bool Available, string Details) Probe()
    {
        var notes = new List<string>();
        foreach (var name in WrapperTypeNames)
        {
            var type = ResolveWrapperType(name);
            if (type is null)
            {
                continue;
            }

            if (!TryGetClsid(type, out var guid))
            {
                notes.Add(type.Name + " carries no CLSID attribute.");
                continue;
            }

            var guidText = guid.ToString("B").ToUpperInvariant();
            if (TryInstantiate(guid))
            {
                return (true, type.Name + " coclass " + guidText + " instantiates via CoCreateInstance (registry-free).");
            }

            var registered = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\" + guidText) is not null;
            notes.Add(type.Name + " " + guidText + " failed to instantiate"
                + (registered ? " (CLSID registered)" : " (CLSID not registered)") + ".");
        }

        var details = "No supported MsRdpClient COM coclass could be instantiated via CoCreateInstance.";
        if (notes.Count > 0)
        {
            details += " " + string.Join(" ", notes);
        }

        // Newer Windows builds register the coclass under new ProgIDs but keep the CLSIDs.
        if (Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"MsRDP.MsRDP") is not null
            || Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"MsRdp.Client") is not null)
        {
            details += " RDP ProgIDs are registered, but registry-free activation still failed.";
        }

        return (false, details);
    }

    /// <summary>
    /// The CLSID an AxHost wrapper will activate. tlbimp puts the coclass GUID in the
    /// INTERNAL System.Windows.Forms.AxHost+ClsidAttribute of the wrapper (its readable
    /// property is "Value") — NOT a CoClassAttribute — so the attribute is matched by
    /// name through reflection.
    /// </summary>
    internal static bool TryGetClsid(Type axWrapperType, out Guid clsid)
    {
        clsid = Guid.Empty;
        foreach (var attribute in axWrapperType.GetCustomAttributes(false))
        {
            var attributeType = attribute.GetType();
            if (!string.Equals(attributeType.Name, "ClsidAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var propertyName in new[] { "Value", "Clsid" })
            {
                var value = attributeType.GetProperty(propertyName)?.GetValue(attribute) as string;
                if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
                {
                    clsid = parsed;
                    return true;
                }
            }
        }

        // Fall back to a CoClassAttribute if a wrapper ever carries one.
        var coclass = axWrapperType.GetCustomAttributes(false)
            .OfType<CoClassAttribute>()
            .FirstOrDefault();
        if (coclass is not null)
        {
            clsid = coclass.CoClass.GUID;
            return clsid != Guid.Empty;
        }

        return false;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private const uint CLSCTX_INPROC_SERVER = 1;

    internal static bool TryInstantiate(Guid clsid)
    {
        var riid = IID_IUnknown;
        var hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref riid, out var obj);
        if (hr == 0 && obj != IntPtr.Zero)
        {
            Marshal.Release(obj);
            return true;
        }

        return false;
    }

    private static Type? ResolveWrapperType(string fullName)
    {
        var type = Type.GetType(fullName, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        type = typeof(AxMsRdpClient9NotSafeForScripting).Assembly.GetType(fullName, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(candidate => candidate is not null);
    }

    /// <summary>Version string of the underlying mstscax module, for diagnostics display.</summary>
    public static string ModuleVersion()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "mstscax.dll");
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.ProductVersion ?? info.FileVersion ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
