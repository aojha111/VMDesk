using System.Runtime.InteropServices;
using static VMDesk.Infrastructure.CredentialStore.NativeCredApi;

namespace VMDesk.Infrastructure.CredentialStore;

/// <summary>Bulk credential cleanup (spec §7). Clears every VMDesk/* target.</summary>
public sealed partial class WindowsCredentialStore
{
    public Task ClearAllCredentialsAsync()
    {
        return Task.Run(() =>
        {
            if (!CredEnumerateW(TargetPrefix + "*", 0, out var count, out var indexPtr))
            {
                return; // Nothing to clear.
            }

            try
            {
                for (var i = 0; i < count; i++)
                {
                    var credPtr = Marshal.ReadIntPtr(indexPtr, i * IntPtr.Size);
                    var cred = System.Runtime.InteropServices.Marshal.PtrToStructure<CREDENTIALW>(credPtr);
                    if (cred.TargetName != IntPtr.Zero)
                    {
                        var target = System.Runtime.InteropServices.Marshal.PtrToStringUni(cred.TargetName);
                        if (target is not null)
                        {
                            CredDeleteW(target, CredTypeGeneric, 0);
                        }
                    }
                }
            }
            finally
            {
                CredFree(indexPtr);
            }

            _log.Info("Cleared all VMDesk credentials.");
        });
    }
}
