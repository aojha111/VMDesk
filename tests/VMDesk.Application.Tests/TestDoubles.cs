using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Application.Tests;

/// <summary>
/// Scriptable IRemoteSession fake: records commands, lets tests raise state
/// changes, and simulates the connect lifecycle without COM.
/// </summary>
public sealed class FakeSession : IRemoteSession
{
    private ConnectionState _state = ConnectionState.Disconnected;

    public FakeSession(Guid vmId, string vmName)
    {
        VmId = vmId;
        VmName = vmName;
    }

    public Guid VmId { get; }
    public string VmName { get; }
    public SessionHostMode HostMode { get; set; } = SessionHostMode.Closed;
    public object? HostControl => null;
    public SessionCapabilities Capabilities { get; } = new();
    public string? LastError { get; set; }
    public string? TechnicalError { get; set; }

    public ConnectionState State
    {
        get => _state;
        set => _state = value;
    }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SessionErrorEventArgs>? SessionError;

    public int ConnectCalls { get; private set; }
    public int DisconnectCalls { get; private set; }
    public int ReconnectCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public int ActivateCalls { get; private set; }
    public int PrepareControlCalls { get; private set; }
    public int StartConnectCalls { get; private set; }
    public List<CancellationToken> ConnectTokens { get; } = new();

    /// <summary>Overrides the connect behaviour; defaults to a no-op success.</summary>
    public Func<CancellationToken, Task>? OnConnect { get; set; }

    /// <summary>When set, DisconnectAsync throws this after recording the call (simulates a dead handle).</summary>
    public Exception? DisconnectError { get; set; }

    public void PrepareControl() => PrepareControlCalls++;

    public void StartConnect() => StartConnectCalls++;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectCalls++;
        ConnectTokens.Add(cancellationToken);
        State = ConnectionState.Connecting;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
        return OnConnect?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public Task ReconnectAsync()
    {
        ReconnectCalls++;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        DisconnectCalls++;
        State = ConnectionState.Disconnected;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
        if (DisconnectError is not null)
        {
            throw DisconnectError;
        }

        return Task.CompletedTask;
    }

    public void SendCtrlAltDel() { }
    public void SetDisplayMode(DisplayScaleMode mode) { }
    public void SetSmartSizing(bool enabled) { }
    public void ToggleFullscreen() { }
    public void SetClipboardRedirect(bool enabled) { }
    public void SetAudioRedirect(bool enabled) { }
    public void SetDriveRedirection(bool enabled, string drives) { }
    public void Activate() => ActivateCalls++;
    public IReadOnlyList<string> GetRemoteClipboardFiles() => Array.Empty<string>();

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }

    public void ReachConnected()
    {
        State = ConnectionState.Connected;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
    }

    public void Fail(string message)
    {
        State = ConnectionState.Failed;
        LastError = message;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, message));
        SessionError?.Invoke(this, new SessionErrorEventArgs(message, "FakeSession failure"));
    }
}

/// <summary>Fake engine that hands out scripted sessions.</summary>
public sealed class FakeEngine : IRemoteSessionEngine
{
    private readonly Func<VirtualMachineEntity, string?, IRemoteSession> _factory;

    public FakeEngine(Func<VirtualMachineEntity, string?, IRemoteSession> factory) => _factory = factory;

    public string Name => "Fake RDP";
    public bool Available { get; set; } = true;

    public Task<IRemoteSession> CreateSessionAsync(VirtualMachineEntity vm, string? credentialReference = null) =>
        Task.FromResult(_factory(vm, credentialReference));

    public Task<RdpAvailabilityInfo> CheckAvailabilityAsync() =>
        Task.FromResult(new RdpAvailabilityInfo(Available, Available ? "fake available" : "fake unavailable", "1.0"));
}

/// <summary>Fake orchestrator that records connect calls and can fail or cancel them.</summary>
public sealed class FakeOrchestrator : IConnectionOrchestrator
{
    public int Calls { get; private set; }
    public List<(IRemoteSession Session, VirtualMachineEntity Vm)> ConnectCalls { get; } = new();
    public Func<IRemoteSession, VirtualMachineEntity, CancellationToken, Task>? OnConnect { get; set; }

    public Task ConnectAsync(IRemoteSession session, VirtualMachineEntity vm, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Calls++;
        ConnectCalls.Add((session, vm));
        return OnConnect?.Invoke(session, vm, cancellationToken) ?? Task.CompletedTask;
    }
}
