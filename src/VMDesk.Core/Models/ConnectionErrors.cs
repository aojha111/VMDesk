using VMDesk.Core.Entities;

namespace VMDesk.Core.Models;

/// <summary>Maps raw connect/disconnect errors to friendly text (spec §17) without leaking secrets.</summary>
public static class ConnectionErrors
{
    public static string Sanitize(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Length > 300)
        {
            msg = msg[..300];
        }

        return msg.ReplaceLineEndings(" ");
    }

    public static string ToFriendly(Exception? ex, VirtualMachineEntity vm)
    {
        if (ex is TimeoutException)
        {
            return $"Connection timed out after {Math.Clamp(vm.ConnectionTimeoutSeconds, 5, 600)} seconds.";
        }

        if (ex is OperationCanceledException)
        {
            return "Connection cancelled.";
        }

        var raw = (ex?.Message ?? "Unknown error.") + " " + (ex?.InnerException?.Message ?? string.Empty);

        if (Contains(raw, "no such host", "getaddrinfo", "name or service", "resolve"))
        {
            return "Unable to resolve host.";
        }

        if (Contains(raw, "refused"))
        {
            return "Connection refused.";
        }

        if (Contains(raw, "unreachable", "network", "socket error"))
        {
            return "Network unavailable.";
        }

        if (Contains(raw, "logon", "credentials", "authentication", "or password", "1326"))
        {
            return "Authentication failed. Check username and password.";
        }

        if (Contains(raw, "nla", "credssp"))
        {
            return "NLA failure. Verify credentials and network level authentication settings.";
        }

        if (Contains(raw, "gateway", "ts gateway"))
        {
            return "RDP gateway unavailable.";
        }

        if (Contains(raw, "timeout", "timed out"))
        {
            return "Connection timeout.";
        }

        if (Contains(raw, "disconnected", "terminated"))
        {
            return "Remote computer disconnected.";
        }

        return "Unable to connect. See technical details for more information.";
    }

    /// <summary>Map an RDP disconnect reason code to friendly text.</summary>
    public static string FromDisconnectReason(long reason)
    {
        return reason switch
        {
            0 => "Disconnected.",
            1 => "Disconnected locally.",
            2 => "Disconnected by remote user.",
            3 => "Disconnected by server (another session or policy).",
            4 => "Disconnected by server (another connection replaced this one).",
            5 => "Disconnected: idle timeout on the server.",
            6 => "Disconnected: server forced timeout.",
            7 => "Disconnected: protocol error.",
            260 => "Disconnected: DNS name lookup failed.",
            516 => "Disconnected: out of memory on the client.",
            522 => "Disconnected: connection timed out.",
            563 => "Disconnected: unable to reach the host.",
            566 => "Disconnected: host found but not listening on RDP port.",
            620 => "Disconnected: fatal protocol error.",
            624 => "Disconnected: connection failed with socket error.",
            2308 => "Disconnected: socket closed unexpectedly.",
            2311 => "Disconnected: decompression error.",
            2314 => "Disconnected: the server session ended.",
            2317 => "Disconnected: unable to negotiate protocol security.",
            _ => $"Disconnected (code {reason})."
        };
    }

    private static bool Contains(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
