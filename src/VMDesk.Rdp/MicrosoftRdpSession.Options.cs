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
    internal void CreateAndConfigureControl()
    {
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
            _log.Info("RDP ActiveX control created and configured.");
        }
        catch (Exception ex)
        {
            ReportError("Failed to start the Remote Desktop component.", ex.Message);
            _connectTcs?.TrySetException(new VmConnectionException(
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
        advanced.RDPPort = p.Port;
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

    internal void SafeConnect()
    {
        try
        {
            _client?.Connect();
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
