namespace VMDesk.Core.Models;

/// <summary>
/// Raised when a connection attempt fails after all retries (spec §57, §82).
/// Carries a user-facing message; the technical cause stays in the inner exception
/// and in the log, never in credential material (spec §30).
/// </summary>
public sealed class VmConnectionException : Exception
{
    public VmConnectionException(string message, Exception? inner) : base(message, inner)
    {
    }
}
