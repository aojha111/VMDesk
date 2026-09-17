using System.Runtime.InteropServices;

namespace VMDesk.Infrastructure.CredentialStore;

/// <summary>P/Invoke surface for the Windows Credential Manager (advapi32).</summary>
internal static class NativeCredApi
{
    internal const int CredTypeGeneric = 1;
    internal const int CredPersistEnterprise = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CREDENTIALW
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    internal static extern bool CredWriteW(ref CREDENTIALW credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    internal static extern bool CredReadW(string target, int type, int flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    internal static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredEnumerateW")]
    internal static extern bool CredEnumerateW(string filter, int flags, out int count, out IntPtr indexPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    internal static extern void CredFree(IntPtr buffer);
}
