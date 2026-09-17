using System.Runtime.InteropServices;

namespace VMDesk.Rdp.Interop;

/// <summary>
/// Sends a Secure Attention Sequence to the focused RDP session (spec §15).
/// The Microsoft RDP client translates the local Ctrl+Alt+End combination into a
/// remote Ctrl+Alt+Del, which is the only supported way for a client control to
/// trigger SAS (a remote Ctrl+Alt+Del cannot be injected into the guest session
/// through the clipboard or the key channel). The RDP connection bar (spec §9)
/// also exposes the same command natively.
/// </summary>
public static class SasSender
{
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;   // Alt
    private const ushort VkEnd = 0x23;
    private const uint KeyEventKeyUp = 0x0002;

    public static void SendCtrlAltEnd()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkEnd, 0, 0, UIntPtr.Zero);
        keybd_event(VkEnd, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(ushort bVk, ushort bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>True when the current process can send input to the desktop (always true for an interactive app).</summary>
    public static bool IsAvailable() => Environment.UserInteractive;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
