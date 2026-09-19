using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>
/// Multi-session workspace manager (spec §13, §43, §76, §102).
/// Owns the set of live sessions. Enforces one session per VM unless a new
/// explicit session is requested. Handles embedded ↔ standalone transitions.
/// </summary>
public sealed class RemoteSessionManager
{
    private readonly IRemoteSessionEngine _engine;
    private readonly IConnectionOrchestrator _orchestrator;
    private readonly IAppLog _log;
    private readonly object _gate = new();
    private readonly List<IRemoteSession> _sessions = new();

    public RemoteSessionManager(IRemoteSessionEngine engine, IConnectionOrchestrator orchestrator, IAppLogFactory logFactory)
    {
        _engine = engine;
        _orchestrator = orchestrator;
        _log = logFactory.GetLogger("SessionManager");
    }

    public IReadOnlyList<IRemoteSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _sessions.ToList();
            }
        }
    }

    public event EventHandler<IRemoteSession>? SessionAdded;
    public event EventHandler<IRemoteSession>? SessionClosed;

    /// <summary>True when the Microsoft RDP ActiveX control is registered and loadable.</summary>
    public Task<RdpAvailabilityInfo> CheckEngineAvailabilityAsync() => _engine.CheckAvailabilityAsync();

    public IRemoteSession? FindByVm(Guid vmId)
    {
        lock (_gate)
        {
            return _sessions.FirstOrDefault(s => s.VmId == vmId);
        }
    }

    /// <summary>Connects a VM. Activates an existing session when one already exists (spec §43).</summary>
    public async Task<IRemoteSession> ConnectAsync(
        VirtualMachineEntity vm,
        string? credentialReference = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // A session that failed or dropped cannot be reused: a new attempt needs a
        // fresh control, so replace it instead of returning a dead handle (spec §57).
        var existing = FindByVm(vm.Id);
        if (existing is not null && existing.State is not ConnectionState.Connected and not ConnectionState.Connecting)
        {
            _log.Info($"Replacing stale {existing.State} session for VM '{vm.Name}'.");
            await CloseAsync(existing);
            existing = null;
        }

        if (existing is not null)
        {
            existing.Activate();
            return existing;
        }

        _log.Info($"Creating RDP session for VM '{vm.Name}' ({vm.Host}:{vm.Port}).");
        var session = await _engine.CreateSessionAsync(vm, credentialReference);
        lock (_gate)
        {
            _sessions.Add(session);
        }

        session.StateChanged += OnSessionStateChanged;
        session.SessionError += OnSessionError;
        SessionAdded?.Invoke(this, session);

        try
        {
            await _orchestrator.ConnectAsync(session, vm, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // User cancelled: tear the half-open session down so a retry starts clean.
            _log.Info($"Connection to VM '{vm.Name}' cancelled; closing pending session.");
            await CloseAsync(session);
            throw;
        }
        catch (Exception ex)
        {
            // Keep the failed session in the workspace (spec §57) but surface the
            // failure to the caller so the UI can report it instead of showing a
            // disconnected window that looks connected.
            _log.Warn($"Connection failed for VM '{vm.Name}': {ConnectionErrors.Sanitize(ex)}");
            throw;
        }

        return session;
    }

    public async Task ReconnectAsync(IRemoteSession session) => await session.ReconnectAsync();

    public async Task DisconnectAsync(IRemoteSession session) => await session.DisconnectAsync();

    public async Task DisconnectAllAsync()
    {
        var snapshot = Sessions;
        foreach (var s in snapshot)
        {
            try
            {
                await s.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _log.Warn($"Disconnect failed for '{s.VmName}': {ex.Message}");
            }
        }
    }

    /// <summary>Closes a session if it is still owned by the workspace; no-op otherwise.</summary>
    public async Task CloseAsync(IRemoteSession session)
    {
        var owned = false;
        lock (_gate)
        {
            owned = _sessions.Remove(session);
        }

        if (!owned)
        {
            return; // Already closed (e.g. via CloseAllAsync during app shutdown).
        }

        try
        {
            await session.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _log.Warn($"Disconnect during close failed for '{session.VmName}': {ex.Message}");
        }

        session.StateChanged -= OnSessionStateChanged;
        session.SessionError -= OnSessionError;
        await session.DisposeAsync();

        SessionClosed?.Invoke(this, session);
        _log.Info($"Closed session '{session.VmName}'.");
    }

    public async Task CloseAllAsync()
    {
        var snapshot = Sessions;
        foreach (var s in snapshot)
        {
            await CloseAsync(s);
        }
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (sender is IRemoteSession session)
        {
            _log.Info($"Session '{session.VmName}' state: {e.State}.");
        }
    }

    private void OnSessionError(object? sender, SessionErrorEventArgs e)
    {
        if (sender is IRemoteSession session)
        {
            _log.Error($"Session '{session.VmName}' error: {e.FriendlyMessage}");
        }
    }
}
