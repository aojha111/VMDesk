namespace VMDesk.Core.Models;

/// <summary>
/// Naming convention for Windows Credential Manager entries (spec §24).
/// Lives in Core so every layer derives the same reference without a password ever
/// being written to the database or the log.
/// </summary>
public static class CredentialNaming
{
    public const string TargetPrefix = "VMDesk/";

    /// <summary>Credential Manager target name for a VM id, e.g. "VMDesk/6f1c...".</summary>
    public static string For(Guid vmId) => TargetPrefix + vmId.ToString("B");

    /// <summary>Returns true when the reference looks like a VMDesk-managed entry.</summary>
    public static bool IsManaged(string? reference) =>
        !string.IsNullOrEmpty(reference) &&
        reference.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase);
}