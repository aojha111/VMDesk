using System.Runtime.InteropServices;
using MSTSCLib;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;

namespace VMDesk.Rdp;

/// <summary>Session commands part 2: redirection toggles, resize, dispose, helpers.</summary>
public sealed partial class MicrosoftRdpSession
{
    public void SetClipboardRedirect(bool enabled)
    {
        OnUiThread(() =>
        {
            if (_client?.AdvancedSettings is IMsRdpClientAdvancedSettings8 advanced)
            {
                RcwHelper.TrySet(advanced, "RedirectClipboard", enabled);
            }
        });
    }

    public void SetAudioRedirect(bool enabled)
    {
        OnUiThread(() =>
        {
            if (_client?.SecuredSettings is IMsRdpClientSecuredSettings secured)
            {
                // 0 = play locally, 1 = play on remote, 2 = do not play
                RcwHelper.TrySet(secured, "AudioRedirectionMode", enabled ? 0 : 2);
            }
        });
    }

    public void SetDriveRedirection(bool enabled, string drives)
    {
        OnUiThread(() =>
        {
            if (_client?.AdvancedSettings is IMsRdpClientAdvancedSettings8 advanced)
            {
                advanced.RedirectDrives = enabled;
            }
        });
    }

    public void Activate()
    {
        OnUiThread(() => _control?.Focus());
    }

    public IReadOnlyList<string> GetRemoteClipboardFiles()
    {
        // The ActiveX control does not expose the remote clipboard file list locally.
        // File transfer uses redirected drives and the clipboard channel instead
        // (spec §106: no fake behaviour).
        return Array.Empty<string>();
    }

    /// <summary>Live resize in native mode (no reconnect) via IMsRdpClient9 (spec §11).</summary>
    internal void UpdateSessionDisplaySize(int width, int height)
    {
        OnUiThread(() =>
        {
            if (_client is null || State != ConnectionState.Connected)
            {
                return;
            }

            try
            {
                _client.UpdateSessionDisplaySettings(
                    (uint)width,
                    (uint)height,
                    (uint)width,
                    (uint)height,
                    0,
                    100,
                    100);
                _log.Info("Session display updated to " + width + "x" + height + ".");
            }
            catch (COMException ex)
            {
                _log.Warn("UpdateSessionDisplaySettings failed: " + ex.Message);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnUiThread(() =>
        {
            try
            {
                if (_client is { Connected: 1 })
                {
                    _client.Disconnect();
                }
            }
            catch
            {
                // Best effort during teardown.
            }

            DetachEventWiring();

            if (_control is not null)
            {
                _control.Dispose();
                _control = null;
            }

            if (_client is not null)
            {
                Marshal.ReleaseComObject(_client);
                _client = null;
            }
        });

        await Task.CompletedTask;
    }

    internal void OnUiThread(Action action)
    {
        var ui = _ui;
        if (ui is null || SynchronizationContext.Current == ui)
        {
            action();
            return;
        }

        ui.Send(_ => action(), null);
    }

    internal void ReportError(string friendly, string technical)
    {
        LastError = friendly;
        TechnicalError = technical;
        _log.Error(friendly + " :: " + technical);
        SessionError?.Invoke(this, new SessionErrorEventArgs(friendly, technical));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MicrosoftRdpSession));
        }
    }
}
