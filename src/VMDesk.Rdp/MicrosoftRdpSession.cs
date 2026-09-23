using System.Runtime.InteropServices;
using AxMSTSCLib;
using MSTSCLib;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;

namespace VMDesk.Rdp;

/// <summary>Session fields, constructor, connect lifecycle, host-thread marshalling.</summary>
public sealed partial class MicrosoftRdpSession : IRemoteSession
{
    private readonly IAppLog _log;
    private readonly Func<Task<CredentialData?>> _credentialResolver;
    private readonly SynchronizationContext? _ui;
    internal AxHost? _control;
    internal Type? _controlType;
    internal IMsRdpClient9? _client;
    private int _stateValue = (int)ConnectionState.Disconnected;
    private SessionHostMode _hostMode = SessionHostMode.Closed;
    private bool _disposed;
    private bool _prepared;
    private TaskCompletionSource<bool>? _connectTcs;
    private DisplayScaleMode _displayMode = DisplayScaleMode.SmartFit;
    private int _remoteWidth;
    private int _remoteHeight;
    internal PendingOptions Pending = new();

    public MicrosoftRdpSession(Guid vmId, string vmName, IAppLogFactory logFactory, Func<Task<CredentialData?>> credentialResolver)
    {
        VmId = vmId;
        VmName = vmName;
        _log = logFactory.GetLogger("Rdp:" + vmName);
        _credentialResolver = credentialResolver;
        _ui = SynchronizationContext.Current;
    }

    public Guid VmId { get; }
    public string VmName { get; }
    public SessionCapabilities Capabilities { get; } = new();
    public string? LastError { get; private set; }
    public string? TechnicalError { get; private set; }

    public ConnectionState State
    {
        get => (ConnectionState)Interlocked.CompareExchange(ref _stateValue, 0, 0);
        private set => Interlocked.Exchange(ref _stateValue, (int)value);
    }

    public SessionHostMode HostMode
    {
        get => _hostMode;
        set => _hostMode = value;
    }

    /// <summary>The WinForms AxHost control wrapped for WPF hosting. Created on the UI thread.</summary>
    public object? HostControl => _control;

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SessionErrorEventArgs>? SessionError;

    /// <summary>The control asked the container window to go fullscreen (spec §11).</summary>
    public event EventHandler? FullscreenRequested;

    /// <summary>The control asked the container window to leave fullscreen.</summary>
    public event EventHandler? FullscreenExitRequested;

    /// <summary>The control asked the container window to minimize.</summary>
    public event EventHandler? ContainerMinimizeRequested;

    /// <summary>Remote desktop resolution changed (spec §11 smart resize).</summary>
    public event EventHandler<RemoteSizeChangedEventArgs>? RemoteSizeChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (State is ConnectionState.Connecting or ConnectionState.Connected)
        {
            return;
        }

        var cred = await _credentialResolver();
        if (cred is null)
        {
            throw new VmConnectionException("No saved credential for this VM. Edit the VM and save the password.", null);
        }

        Pending.Password = cred.Password;
        Pending.Username = cred.Username;

        _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() =>
        {
            _connectTcs.TrySetCanceled(cancellationToken);
            OnUiThread(DisconnectControl);
        });

        // Parent-before-connect ordering: when the manager already prepared (and the
        // caller parented) the control, skip creation and dial the prepared control.
        // ApplyOptions re-runs so the credentials resolved here — after preparation —
        // actually reach the control before Connect().
        if (!_prepared)
        {
            OnUiThread(PrepareControlCore);
        }
        else
        {
            OnUiThread(ApplyOptions);
        }
        State = ConnectionState.Connecting;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
        OnUiThread(StartConnectCore);

        try
        {
            await _connectTcs.Task.ConfigureAwait(false);
            // Connect telemetry: one Info line per resolution path so field hangs
            // (never-resolved TCS) and cancels are diagnosable from the log alone.
            _log.Info($"Connect to '{VmName}' resolved: the control reported a successful handshake.");
        }
        catch (OperationCanceledException)
        {
            _log.Info($"Connect to '{VmName}' resolved by cancellation before the handshake completed; the dial was abandoned.");
            throw;
        }
        finally
        {
            _connectTcs = null;
        }
    }
}
