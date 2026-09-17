using VMDesk.Core.Enums;

namespace VMDesk.Core.Models;

public sealed record CredentialData(string Reference, string Username, string Password);
public sealed record SavedCredential(string Reference, string Username, DateTimeOffset LastWritten);

public sealed record ConnectionTestResult(
    bool Success,
    bool DnsResolved,
    bool TcpReachable,
    int? LatencyMs,
    string? Error);

public enum VmOnlineState { Unknown, Online, Offline }

public sealed record RdpAvailabilityInfo(bool Available, string? Details, string? ClientVersion);

public sealed record LayoutRect(double X, double Y, double Width, double Height);

public enum WorkspaceTilingMode
{
    One = 0,
    TwoHorizontal = 1,
    TwoVertical = 2,
    Grid4 = 3,
    Grid6 = 4,
    Auto = 5,
    Stack = 6
}

public sealed record TransferProgress(
    TransferDirection Direction,
    string FileName,
    long BytesTransferred,
    long TotalBytes,
    double BytesPerSecond,
    int PercentComplete)
{
    public bool IsIndeterminate => TotalBytes <= 0;
}

public sealed record UpdateCheckResult(bool Supported, string Message);

public sealed record ImportResult(
    int Imported,
    int Merged,
    int Skipped,
    IReadOnlyList<string> Warnings);
