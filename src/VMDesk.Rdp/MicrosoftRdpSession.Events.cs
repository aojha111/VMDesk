using System.Reflection;
using AxMSTSCLib;
using MSTSCLib;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Rdp;

/// <summary>ActiveX event wiring through the generated CLR events on the AxHost wrapper.</summary>
public sealed partial class MicrosoftRdpSession
{
    private readonly List<(EventInfo Event, Delegate Handler)> _wired = new();

    /// <summary>
    /// Subscribes to the ActiveX wrapper's CLR events. The generated AxHost wrapper
    /// exposes strongly typed events (Add/remove accessors); the concrete wrapper type
    /// depends on the Windows build, so accessors are resolved by reflection.
    /// </summary>
    internal void AttachEvents()
    {
        if (_control is null || _controlType is null)
        {
            return;
        }

        Subscribe("OnConnecting", nameof(OnRdpConnecting));
        Subscribe("OnConnected", nameof(OnRdpConnected));
        Subscribe("OnLoginComplete", nameof(OnRdpLoginComplete));
        Subscribe("OnDisconnected", nameof(OnRdpDisconnected));
        Subscribe("OnLogonError", nameof(OnRdpLogonError));
        Subscribe("OnFatalError", nameof(OnRdpFatalError));
        Subscribe("OnWarning", nameof(OnRdpWarning));
        Subscribe("OnRemoteDesktopSizeChange", nameof(OnRdpRemoteSizeChange));
        Subscribe("OnAutoReconnecting", nameof(OnRdpAutoReconnecting));
        Subscribe("OnEnterFullScreenMode", nameof(OnRdpEnterFullScreen));
        Subscribe("OnLeaveFullScreenMode", nameof(OnRdpLeaveFullScreen));
        Subscribe("OnRequestGoFullScreen", nameof(OnRdpRequestGoFullScreen));
        Subscribe("OnRequestLeaveFullScreen", nameof(OnRdpRequestLeaveFullScreen));
        Subscribe("OnRequestContainerMinimize", nameof(OnRdpRequestContainerMinimize));
    }

    private void Subscribe(string eventName, string handlerName)
    {
        var control = _control;
        var controlType = _controlType;
        if (control is null || controlType is null)
        {
            return;
        }

        try
        {
            var evt = controlType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
            if (evt is null)
            {
                _log.Warn("RDP wrapper has no event " + eventName + "; skipping.");
                return;
            }

            var handler = GetType().GetMethod(handlerName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (handler is null)
            {
                _log.Warn("Handler " + handlerName + " missing; skipping " + eventName + ".");
                return;
            }

            var dlg = Delegate.CreateDelegate(evt.EventHandlerType!, this, handler);
            evt.AddEventHandler(control, dlg);
            _wired.Add((evt, dlg));
        }
        catch (Exception ex)
        {
            _log.Warn("Could not subscribe to " + eventName + ": " + ex.Message);
        }
    }

    internal void DetachEventWiring()
    {
        var control = _control;
        foreach (var (evt, handler) in _wired)
        {
            if (control is null)
            {
                break;
            }

            try
            {
                evt.RemoveEventHandler(control, handler);
            }
            catch
            {
                // Best effort during teardown.
            }
        }

        _wired.Clear();
    }

    private void OnRdpConnecting(object? sender, EventArgs e)
    {
        State = ConnectionState.Connecting;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, "Connecting..."));
    }

    private void OnRdpConnected(object? sender, EventArgs e)
    {
        State = ConnectionState.Connected;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
        _connectTcs?.TrySetResult(true);
    }

    private void OnRdpLoginComplete(object? sender, EventArgs e)
    {
        _log.Info("Logon complete.");
    }

    private void OnRdpDisconnected(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        var wasConnecting = State == ConnectionState.Connecting;
        State = ConnectionState.Disconnected;
        var friendly = ConnectionErrors.FromDisconnectReason(e.discReason);
        LastError = friendly;
        var technical = "OnDisconnected discReason=" + e.discReason;
        TechnicalError = technical;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, friendly));

        if (wasConnecting)
        {
            _connectTcs?.TrySetException(new VmConnectionException(friendly, null));
        }
        else
        {
            SessionError?.Invoke(this, new SessionErrorEventArgs("Connection lost. " + friendly, technical));
        }
    }

    private void OnRdpLogonError(object? sender, IMsTscAxEvents_OnLogonErrorEvent e)
    {
        if (e.lError >= 0)
        {
            return;
        }

        var message = e.lError switch
        {
            -2 => "User session ended by the remote machine (logon timeout).",
            -3 => "Another logon replaced this session.",
            -6 => "Logon aborted by the remote machine.",
            -7 => "Logon failed: credentials rejected.",
            _ => "Logon error " + e.lError + "."
        };

        _log.Warn(message);
        SessionError?.Invoke(this, new SessionErrorEventArgs(message, "OnLogonError(" + e.lError + ")"));
    }
}
