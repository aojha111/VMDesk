using System.Runtime.InteropServices;
using AxMSTSCLib;
using MSTSCLib;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;

namespace VMDesk.Rdp;

/// <summary>Control creation + configuration. UI/STA thread only (spec §35).</summary>
public sealed partial class MicrosoftRdpSession
{
    /// <summary>
    /// Creates, configures and event-wires the ActiveX control on the UI thread.
    /// Does NOT connect. The <c>_prepared</c> flag makes a double call cheap so
    /// later phases can rely on a single preparation (Task 2 ordering).
    /// Marshals itself to the UI thread that constructed the session.
    /// </summary>
    public void PrepareControl() => OnUiThread(PrepareControlCore);

    private void PrepareControlCore()
    {
        if (_prepared)
        {
            return;
        }

        try
        {
            var created = RdpControlFactory.CreateControl();
            _control = created.Control;
            _controlType = created.ControlType;
            _client = _control.GetOcx() as IMsRdpClient9;
            if (_client is null)
            {
                throw new InvalidOperationException("The RDP ActiveX control does not expose IMsRdpClient9.");
            }

            ApplyOptions();
            AttachEvents();
            _prepared = true;
            _log.Info("RDP ActiveX control created and configured.");
        }
        catch (Exception ex)
        {
            ReportError("Failed to start the Remote Desktop component.", ex.Message);
            _connectTcs?.TrySetException(ex as VmConnectionException ?? new VmConnectionException(
                "The Microsoft Remote Desktop component could not be loaded. Run VMDesk diagnostics for details.", ex));
        }
    }

    internal void ApplyOptions()
    {
        var client = _client!;
        var advanced = client.AdvancedSettings as IMsRdpClientAdvancedSettings8
            ?? throw new InvalidOperationException("IMsRdpClientAdvancedSettings8 unavailable.");
        var p = Pending;

        client.Server = p.Host;
        // Port is optional: 0 leaves the ActiveX default (3389) untouched.
        if (p.Port > 0)
        {
            advanced.RDPPort = p.Port;
        }
        client.UserName = p.Username;
        client.Domain = p.Domain;
        client.DesktopWidth = p.ScreenWidth;
        client.DesktopHeight = p.ScreenHeight;
        client.ColorDepth = p.ColorDepth;
        client.ConnectingText = "Connecting to " + p.Host + "...";

        var ocx = _control!.GetOcx()!;
        if (ocx is IMsRdpClientNonScriptable5 ns5)
        {
            ns5.ClearTextPassword = p.Password ?? string.Empty;
            ns5.PromptForCredentials = false;
            ns5.EnableCredSspSupport = true;
            ns5.NegotiateSecurityLayer = true;
            ns5.RedirectDynamicDrives = p.DriveRedirect;
            ns5.WarnAboutClipboardRedirection = false;
            ns5.WarnAboutPrinterRedirection = false;
            ns5.WarnAboutSendingCredentials = false;
            ns5.AllowCredentialSaving = false;
        }

        advanced.SmartSizing = p.SmartSizing;
        advanced.RedirectDrives = p.DriveRedirect;
        advanced.RedirectPrinters = p.PrinterRedirect;
        advanced.RedirectPorts = p.PortRedirect;
        advanced.RedirectSmartCards = p.SmartCardRedirect;
        RcwHelper.TrySet(advanced, "RedirectClipboard", p.ClipboardRedirect);
        advanced.EnableAutoReconnect = p.AutoReconnect;
        advanced.MaxReconnectAttempts = 20;
        advanced.DisplayConnectionBar = p.ConnectionBar;
        advanced.PinConnectionBar = false;
        advanced.ConnectionBarShowMinimizeButton = p.ConnectionBar;
        advanced.ConnectToAdministerServer = p.AdminSession;
        advanced.PerformanceFlags = unchecked((int)PerformanceFlagsMapper.Map(p.PerformancePreset));
        advanced.overallConnectionTimeout = 30;
        advanced.singleConnectionTimeout = 30;
        advanced.shutdownTimeout = 10;
        advanced.keepAliveInterval = 60000;

        if (client.SecuredSettings is IMsRdpClientSecuredSettings secured)
        {
            RcwHelper.TrySet(secured, "KeyboardHookMode", p.KeyboardHookMode switch
            {
                "Remote" => 1,
                "FullScreenOnly" => 2,
                _ => 0
            });
            RcwHelper.TrySet(secured, "AudioRedirectionMode", p.AudioRedirect ? 0 : 2);
            RcwHelper.TrySet(advanced, "AudioCaptureRedirectionMode", p.MicrophoneRedirect ? 1 : 0);
        }

        if (p.UseGateway && client.TransportSettings2 is IMsRdpClientTransportSettings2 transport)
        {
            transport.GatewayHostname = p.GatewayServer;
            transport.GatewayUsageMethod = 1;
            transport.GatewayProfileUsageMethod = 1;
            transport.GatewayCredsSource = 0;
            transport.GatewayCredSharing = 0;
            RcwHelper.TrySet(transport, "GatewayUsername", p.GatewayUsername);
            RcwHelper.TrySet(transport, "GatewayDomain", p.Domain);
        }
    }

    /// <summary>UI thread: starts the connect. A missing or unprepared control is a loud failure, never a silent no-op.</summary>
    public void StartConnect() => OnUiThread(StartConnectCore);

    private void StartConnectCore()
    {
        if (_client is null || !_prepared)
        {
            // PrepareControl already captured a specific cause? Surface it via the awaited task instead.
            if (_connectTcs is { Task.IsFaulted: true })
            {
                return;
            }

            var ex = new VmConnectionException("The RDP control could not be created. Run Diagnostics or use an external client.", null);
            _connectTcs?.TrySetException(ex);
            throw ex;
        }

        SafeConnect();
    }

    internal void SafeConnect()
    {
        if (_client is null)
        {
            var ex = new VmConnectionException("The RDP control could not be created. Run Diagnostics or use an external client.", null);
            _connectTcs?.TrySetException(ex);
            return;
        }

        try
        {
            _client.Connect();
        }
        catch (COMException ex)
        {
            ReportError("Unable to start the RDP connection.", ex.Message);
            _connectTcs?.TrySetException(new VmConnectionException("Unable to start the RDP connection.", ex));
        }
    }

    internal void DisconnectControl()
    {
        try
        {
            if (_client is { Connected: 1 })
            {
                _client.Disconnect();
            }
        }
        catch (COMException ex)
        {
            _log.Warn("Disconnect COM error: " + ex.Message);
        }
    }
}
