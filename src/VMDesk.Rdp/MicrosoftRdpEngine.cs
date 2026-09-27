using AxMSTSCLib;
using MSTSCLib;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;

namespace VMDesk.Rdp;

/// <summary>
/// Microsoft RDP engine: creates sessions backed by the native ActiveX control
/// (spec §1, §9, §66). Credentials come from the Windows Credential Manager at
/// connect time and are never stored or logged by this layer.
/// </summary>
public sealed class MicrosoftRdpEngine : IRemoteSessionEngine
{
    private readonly ICredentialStore _credentialStore;
    private readonly IAppLogFactory _logFactory;
    private readonly IAppLog _log;
    private readonly Func<(bool Available, string Details)> _controlProbe;

    public MicrosoftRdpEngine(ICredentialStore credentialStore, IAppLogFactory logFactory, Func<(bool Available, string Details)>? controlProbe = null)
    {
        _credentialStore = credentialStore;
        _logFactory = logFactory;
        _log = logFactory.GetLogger("RdpEngine");
        _controlProbe = controlProbe ?? Interop.RdpControlFactory.Probe;
    }

    public string Name => "Microsoft RDP (mstscax)";

    public Task<RdpAvailabilityInfo> CheckAvailabilityAsync()
    {
        var (available, details) = Interop.RdpControlFactory.Probe();
        return Task.FromResult(new RdpAvailabilityInfo(available, details, available ? Interop.RdpControlFactory.ModuleVersion() : null));
    }

    public async Task<IRemoteSession> CreateSessionAsync(VirtualMachineEntity vm, string? credentialReference = null)
    {
        // Discovered Hyper-V VMs without an RDP address open the local console instead of
        // failing on the missing host: vmconnect.exe takes the provider id, no credentials.
        if (string.Equals(vm.Provider, "HyperV", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(vm.Host)
            && !string.IsNullOrWhiteSpace(vm.ProviderId))
        {
            _log.Info($"No RDP host for Hyper-V VM '{vm.Name}' — opening the console via vmconnect.exe.");
            return new VmConnectExternalSession(vm.Id, vm.Name, vm.ProviderId, _logFactory);
        }

        // Sessions are always hosted by the app itself (embedded workspace or the app's
        // own session window). Handing the connection to the external mstsc.exe client
        // is forbidden, so an unavailable control fails with an actionable error.
        var (available, details) = _controlProbe();
        if (!available)
        {
            _log.Error($"Cannot create the in-app RDP session: {details}");
            throw new VmConnectionException(
                "The Microsoft Remote Desktop ActiveX control is not available, so the session cannot be shown inside VMDesk.\n\n"
                + $"Details: {details}\n\n"
                + "Open Diagnostics for the detailed RDP availability report.",
                null);
        }

        var reference = string.IsNullOrEmpty(credentialReference)
            ? (string.IsNullOrEmpty(vm.CredentialReference)
            ? CredentialNaming.For(vm.Id)
            : vm.CredentialReference)
            : credentialReference;

        var store = _credentialStore;
        var log = _log;
        var session = new MicrosoftRdpSession(
            vm.Id,
            vm.Name,
            _logFactory,
            async () =>
            {
                try
                {
                    return await store.GetCredentialAsync(reference);
                }
                catch (Exception ex)
                {
                    log.Error("Credential read failed: " + ex.Message);
                    return null;
                }
            });

        ApplyPending(session.Pending, vm);
        return session;
    }

    internal static void ApplyPending(PendingOptions p, VirtualMachineEntity vm)
    {
        var host = vm.Host ?? string.Empty;
        // Port is optional: 0 means "leave the RDP control's default port (3389)".
        var port = vm.Port > 0 ? vm.Port : 0;
        var username = vm.Username ?? string.Empty;
        var domain = vm.Domain ?? string.Empty;

        // DOMAIN\user form: split into domain + user for the ActiveX control.
        if (username.Contains('\\', StringComparison.Ordinal) && domain.Length == 0)
        {
            var slash = username.IndexOf('\\');
            domain = username[..slash];
            username = username[(slash + 1)..];
        }

        p.Host = host;
        p.Port = port;
        p.Username = username;
        p.Domain = domain;
        p.ScreenWidth = Math.Clamp(vm.ScreenWidth, 640, 8192);
        p.ScreenHeight = Math.Clamp(vm.ScreenHeight, 480, 8192);
        p.ColorDepth = vm.ColorDepth;
        p.SmartSizing = vm.SmartSizing;
        p.FullScreen = vm.FullScreen;
        p.UseAllMonitors = vm.UseAllMonitors;
        p.AdminSession = vm.AdminSession;
        p.ClipboardRedirect = vm.ClipboardRedirection;
        p.AudioRedirect = vm.AudioRedirection;
        p.MicrophoneRedirect = vm.MicrophoneRedirection;
        p.DriveRedirect = vm.DriveRedirection;
        p.DriveList = string.IsNullOrWhiteSpace(vm.DrivesToRedirect) ? "*" : vm.DrivesToRedirect;
        p.PrinterRedirect = vm.PrinterRedirection;
        p.SmartCardRedirect = vm.SmartCardRedirection;
        p.PortRedirect = vm.PortRedirection;
        p.KeyboardHookMode = vm.KeyboardHookMode;
        p.ConnectionBar = vm.ConnectionBarEnabled;
        p.PerformancePreset = vm.PerformancePreset;
        p.AutoReconnect = vm.ReconnectAutomatically;
        p.UseGateway = vm.UseGatewayServer;
        p.GatewayServer = vm.GatewayServer;
        p.GatewayUsername = vm.GatewayUsername;
    }
}
