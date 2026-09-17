using System.Runtime.InteropServices;
using MSTSCLib;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Rdp.Interop;

namespace VMDesk.Rdp;

/// <summary>Session commands part 1: reconnect, disconnect, SAS, display modes.</summary>
public sealed partial class MicrosoftRdpSession
{
    public Task ReconnectAsync()
    {
        ThrowIfDisposed();
        if (_client is null)
        {
            return Task.CompletedTask;
        }

        State = ConnectionState.Reconnecting;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
        OnUiThread(() =>
        {
            try
            {
                _client.Reconnect((uint)_client.DesktopWidth, (uint)_client.DesktopHeight);
            }
            catch (COMException ex)
            {
                ReportError("Reconnect failed.", ex.Message);
                State = ConnectionState.Failed;
                StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, "Reconnect failed."));
            }
        });

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        ThrowIfDisposed();
        OnUiThread(DisconnectControl);
        State = ConnectionState.Disconnected;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
        return Task.CompletedTask;
    }

    public void SendCtrlAltDel()
    {
        ThrowIfDisposed();
        OnUiThread(() =>
        {
            // spec §15: the supported client-side SAS path is Ctrl+Alt+End, which the
            // Microsoft RDP client translates into a remote Ctrl+Alt+Del. The connection
            // bar (DisplayConnectionBar) exposes the same command natively.
            _control?.Focus();
            SasSender.SendCtrlAltEnd();
        });
    }

    public void SetDisplayMode(DisplayScaleMode mode)
    {
        _displayMode = mode;
        OnUiThread(() =>
        {
            if (_client is null)
            {
                return;
            }

            if (_client.AdvancedSettings is IMsRdpClientAdvancedSettings8 advanced)
            {
                if (mode == DisplayScaleMode.SmartFit)
                {
                    advanced.SmartSizing = true;
                }
                else if (mode == DisplayScaleMode.Native)
                {
                    advanced.SmartSizing = false;
                }
            }

            if (mode == DisplayScaleMode.Fullscreen && _control is not null)
            {
                RcwHelper.TrySet(_control.GetOcx()!, "FullScreen", true);
            }
        });
    }

    public void SetSmartSizing(bool enabled)
    {
        _displayMode = enabled ? DisplayScaleMode.SmartFit : DisplayScaleMode.Native;
        OnUiThread(() =>
        {
            if (_client?.AdvancedSettings is IMsRdpClientAdvancedSettings8 advanced)
            {
                advanced.SmartSizing = enabled;
            }
        });
    }

    public void ToggleFullscreen()
    {
        ThrowIfDisposed();
        OnUiThread(() =>
        {
            var ocx = _control?.GetOcx();
            if (ocx is null)
            {
                return;
            }

            var current = RcwHelper.TryGet<bool>(ocx, "FullScreen");
            RcwHelper.TrySet(ocx, "FullScreen", !current);
        });
    }
}
