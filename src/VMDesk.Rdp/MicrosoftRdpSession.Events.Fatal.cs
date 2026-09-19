using AxMSTSCLib;
using MSTSCLib;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Rdp;

/// <summary>Warning / fatal-error / reconnect / fullscreen events.</summary>
public sealed partial class MicrosoftRdpSession
{
    private void OnRdpFatalError(object? sender, IMsTscAxEvents_OnFatalErrorEvent e)
    {
        var message = DescribeFatalError(e.errorCode);
        ReportError(message, "OnFatalError(" + e.errorCode + ")");
        State = ConnectionState.Failed;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, message));
        _connectTcs?.TrySetException(new VmConnectionException(message, null));
    }

    private void OnRdpWarning(object? sender, IMsTscAxEvents_OnWarningEvent e)
    {
        _log.Info("RDP warning " + e.warningCode + ".");
    }

    private void OnRdpAutoReconnecting(object? sender, IMsTscAxEvents_OnAutoReconnectingEvent e)
    {
        State = ConnectionState.Reconnecting;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, "Reconnecting (attempt " + e.attemptCount + ")..."));
    }

    private void OnRdpEnterFullScreen(object? sender, EventArgs e)
    {
        _displayMode = DisplayScaleMode.Fullscreen;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
    }

    private void OnRdpLeaveFullScreen(object? sender, EventArgs e)
    {
        _displayMode = DisplayScaleMode.SmartFit;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
    }

    private void OnRdpRequestGoFullScreen(object? sender, EventArgs e)
    {
        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRdpRequestLeaveFullScreen(object? sender, EventArgs e)
    {
        FullscreenExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRdpRequestContainerMinimize(object? sender, EventArgs e)
    {
        ContainerMinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRdpRemoteSizeChange(object? sender, IMsTscAxEvents_OnRemoteDesktopSizeChangeEvent e)
    {
        _remoteWidth = e.width;
        _remoteHeight = e.height;
        RemoteSizeChanged?.Invoke(this, new RemoteSizeChangedEventArgs(e.width, e.height));
    }

    /// <summary>Current remote desktop size in pixels, as reported by the RDP control.</summary>
    public int RemoteDesktopWidth => _remoteWidth;

    public int RemoteDesktopHeight => _remoteHeight;

    private static string DescribeFatalError(int code)
    {
        return code switch
        {
            1 => "Unknown internal RDP error.",
            2 or 6 => "RDP: out of memory.",
            3 => "RDP: connection timed out.",
            4 or 8 => "RDP: socket connection error.",
            5 or 52 or 53 => "RDP: cannot resolve the host name.",
            7 => "RDP: protocol error (connection timed out).",
            9 => "RDP: protocol error (invalid data).",
            10 => "RDP: socket closed.",
            11 or 16 or 17 or 20 or 21 or 22 => "RDP: internal security error.",
            12 or 13 or 14 => "RDP: licensing error.",
            18 => "RDP: terminal server could not be reached.",
            19 => "RDP: terminal server session ended unexpectedly.",
            26 or 30 or 31 or 35 or 36 => "RDP: security data error.",
            29 => "RDP: forced disconnect by an administrator.",
            41 => "RDP: disconnect due to idle timeout.",
            43 => "RDP: disconnection due to server policy.",
            50 => "RDP: the computer name contains invalid characters.",
            51 => "RDP: SSL encryption error.",
            55 => "RDP: SSL error - certificate not trusted.",
            56 => "RDP: SSL error - certificate expired.",
            57 => "RDP: SSL error - certificate name mismatch.",
            58 => "RDP: SSL error - certificate revoked.",
            59 => "RDP: SSL error - no certificate available.",
            60 => "RDP: SSL error - policy forbids the connection.",
            62 or 85 => "RDP: the remote computer requires Network Level Authentication.",
            63 => "RDP: authentication error.",
            64 => "RDP: the remote computer is not listening on the RDP port.",
            66 => "RDP: SSL error - certificate invalid.",
            72 => "RDP: could not negotiate a common security protocol.",
            76 => "RDP: the remote computer does not support FIPS.",
            91 => "RDP: CredSSP target principal mismatch.",
            105 => "RDP: gateway connection failed.",
            116 => "RDP: gateway target unreachable.",
            131 => "RDP: connection timed out.",
            _ => "RDP fatal error " + code + "."
        };
    }
}
