using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

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
    /// Each candidate is instantiated for real so an unregistered coclass fails fast.
    /// </summary>
    public static RdpControl CreateControl()
    {
        var lastError = "no candidate succeeded";
        foreach (var name in WrapperTypeNames)
        {
            var type = Type.GetType(name, throwOnError: false);
            if (type is null)
            {
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
                lastError = name + ": " + ex.Message;
            }
            catch (MissingMethodException ex)
            {
                lastError = name + ": " + ex.Message;
            }
        }

        throw new InvalidOperationException(
            "The Microsoft Remote Desktop ActiveX control (mstscax.dll) is not available. " + lastError);
    }

    /// <summary>Registry-only availability probe used by diagnostics (spec §56).</summary>
    public static (bool Available, string Details) Probe()
    {
        foreach (var name in WrapperTypeNames)
        {
            var type = Type.GetType(name, throwOnError: false);
            if (type is null)
            {
                continue;
            }

            var coclassAttribute = type.GetCustomAttributes(false)
                .OfType<CoClassAttribute>()
                .FirstOrDefault();
            if (coclassAttribute is null)
            {
                continue;
            }

            var guid = coclassAttribute.CoClass.GUID.ToString("B").ToUpperInvariant();
            if (Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\" + guid) is not null)
            {
                return (true, type.Name + " coclass " + guid + " is registered.");
            }
        }

        // Newer Windows builds register the coclass under new ProgIDs but keep the CLSIDs.
        if (Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"MsRDP.MsRDP") is not null
            || Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"MsRdp.Client") is not null)
        {
            return (true, "RDP ActiveX coclass registered via MsRDP.MsRDP / MsRdp.Client ProgID.");
        }

        return (false, "No supported MsRdpClient COM coclass is registered (mstscax.dll).");
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
