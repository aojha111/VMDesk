using System.Runtime.InteropServices;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using static VMDesk.Infrastructure.CredentialStore.NativeCredApi;

namespace VMDesk.Infrastructure.CredentialStore;

/// <summary>
/// Windows Credential Manager implementation of ICredentialStore (spec §7).
/// Passwords live only inside Windows DPAPI-protected credential storage; SQLite
/// stores only the credential reference string. No password material is logged.
/// </summary>
public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const string TargetPrefix = "VMDesk/";
    private readonly IAppLog _log;

    public WindowsCredentialStore(IAppLogFactory logFactory)
    {
        _log = logFactory.GetLogger("CredentialStore");
    }

    public Task<bool> IsAvailableAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                // A read of a nonexistent probe must return ERROR_NOT_FOUND (1168), proving the API works.
                CredReadW(TargetPrefix + "nonexistent-probe", CredTypeGeneric, 0, out _);
                return Marshal.GetLastWin32Error() == 1168;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        });
    }

    public Task SaveCredentialAsync(string reference, string username, string password)
    {
        return Task.Run(() =>
        {
            AssertReference(reference);
            var blob = System.Text.Encoding.Unicode.GetBytes(password);
            var cred = new CREDENTIALW
            {
                Type = CredTypeGeneric,
                TargetName = Marshal.StringToCoTaskMemUni(reference),
                Comment = Marshal.StringToCoTaskMemUni("VMDesk saved credential"),
                CredentialBlobSize = blob.Length,
                CredentialBlob = Marshal.AllocCoTaskMem(blob.Length),
                Persist = CredPersistEnterprise,
                UserName = Marshal.StringToCoTaskMemUni(username)
            };

            try
            {
                Marshal.Copy(blob, 0, cred.CredentialBlob, blob.Length);
                if (!CredWriteW(ref cred, 0))
                {
                    throw new InvalidOperationException($"CredWrite failed with error {Marshal.GetLastWin32Error()}.");
                }
            }
            finally
            {
                FreeCred(cred);
            }

            _log.Info("Saved credential (target details withheld from log).");
        });
    }

    public Task<CredentialData?> GetCredentialAsync(string reference)
    {
        return Task.Run(() =>
        {
            if (!CredReadW(reference, CredTypeGeneric, 0, out var credPtr))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1168) // ERROR_NOT_FOUND
                {
                    return null;
                }

                throw new InvalidOperationException($"CredRead failed with error {error}.");
            }

            try
            {
                return ReadCredential(credPtr, reference);
            }
            finally
            {
                CredFree(credPtr);
            }
        });
    }

    public Task DeleteCredentialAsync(string reference)
    {
        return Task.Run(() =>
        {
            if (!CredDeleteW(reference, CredTypeGeneric, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1168)
                {
                    return; // Already gone.
                }

                throw new InvalidOperationException($"CredDelete failed with error {error}.");
            }

            _log.Info("Deleted credential (target details withheld from log).");
        });
    }

    public async Task<bool> CredentialExistsAsync(string reference)
    {
        return await GetCredentialAsync(reference) is not null;
    }

    public Task<IReadOnlyList<SavedCredential>> GetCredentialsAsync()
    {
        return Task.Run<IReadOnlyList<SavedCredential>>(() =>
        {
            if (!CredEnumerateW(TargetPrefix + "*", 0, out var count, out var indexPtr))
            {
                return Array.Empty<SavedCredential>();
            }

            var result = new List<SavedCredential>(count);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var credPtr = Marshal.ReadIntPtr(indexPtr, i * IntPtr.Size);
                    var cred = Marshal.PtrToStructure<CREDENTIALW>(credPtr);
                    var reference = cred.TargetName == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(cred.TargetName) ?? string.Empty;
                    var username = cred.UserName == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(cred.UserName) ?? string.Empty;
                    if (reference.StartsWith(TargetPrefix, StringComparison.Ordinal))
                    {
                        var fileTime = ((long)cred.LastWritten.dwHighDateTime << 32) | (uint)cred.LastWritten.dwLowDateTime;
                        var written = DateTime.FromFileTimeUtc(fileTime);
                        result.Add(new SavedCredential(reference, username, new DateTimeOffset(written)));
                    }
                }
            }
            finally
            {
                CredFree(indexPtr);
            }

            return result.OrderByDescending(x => x.LastWritten).ToList();
        });
    }

    private static void AssertReference(string reference)
    {
        if (!reference.StartsWith(TargetPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Credential reference must start with 'VMDesk/'.", nameof(reference));
        }
    }

    private static CredentialData ReadCredential(IntPtr credPtr, string reference)
    {
        var cred = Marshal.PtrToStructure<CREDENTIALW>(credPtr);
        var username = cred.UserName != IntPtr.Zero ? Marshal.PtrToStringUni(cred.UserName) ?? string.Empty : string.Empty;
        var password = string.Empty;
        if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
        {
            var blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
            password = System.Text.Encoding.Unicode.GetString(blob).TrimEnd('\0');
        }

        return new CredentialData(reference, username, password);
    }

    private static void FreeCred(CREDENTIALW cred)
    {
        if (cred.TargetName != IntPtr.Zero) Marshal.FreeCoTaskMem(cred.TargetName);
        if (cred.Comment != IntPtr.Zero) Marshal.FreeCoTaskMem(cred.Comment);
        if (cred.CredentialBlob != IntPtr.Zero) Marshal.FreeCoTaskMem(cred.CredentialBlob);
        if (cred.UserName != IntPtr.Zero) Marshal.FreeCoTaskMem(cred.UserName);
    }
}
