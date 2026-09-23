using System.Diagnostics;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;

namespace VMDesk.Rdp;

/// <summary>
/// Hyper-V console session: launches vmconnect.exe (the Virtual Machine Connection snap-in)
/// for a discovered Hyper-V VM that has no RDP address, mirroring the process lifecycle the
/// way <see cref="MstscExternalSession"/> mirrors mstsc. It is deliberately a separate class:
/// MstscExternalSession is hardened against the exit-race/connect-hang problem and must not
/// be generalized to arbitrary executables.
///
/// vmconnect takes positional arguments ([server] vmId); the VM identifier comes from the
/// Hyper-V provider (the Msvm_ComputerSystem Id), never from user text on a command line, and
/// is passed via ProcessStartInfo.ArgumentList. No credential is ever passed to vmconnect.
/// A machine without the Hyper-V management tools has no vmconnect.exe: StartConnect then
/// fails the session with an actionable message and never throws.
/// </summary>
public sealed class VmConnectExternalSession : IRemoteSession
{
    /// <summary>Process seam so the session lifecycle is unit-testable without spawning vmconnect.</summary>
    public interface IExternalConsoleProcess : IDisposable
    {
        bool HasExited { get; }
        event EventHandler? Exited;
        void Kill();
    }

    /// <summary>Real implementation over System.Diagnostics.Process.</summary>
    private sealed class ProcessHandle : IExternalConsoleProcess
    {
        private readonly Process _process;

        public ProcessHandle(ProcessStartInfo startInfo)
        {
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("vmconnect.exe did not start.");
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? Exited;
        public bool HasExited => _process.HasExited;
        public void Kill() => _process.Kill(entireProcessTree: true);
        public void Dispose() => _process.Dispose();
    }

    private readonly object _gate = new();
    private readonly IAppLog _log;
    private readonly string _vmConsoleId;
    private readonly Func<ProcessStartInfo, IExternalConsoleProcess> _processFactory;
    private readonly Func<string?> _exeLocator;
    private IExternalConsoleProcess? _process;
    private bool _started;
    private bool _exited;
    private bool _disconnecting;
    private bool _disposed;

    public VmConnectExternalSession(Guid vmId, string vmName, string vmConsoleId, IAppLogFactory logFactory)
        : this(vmId, vmName, vmConsoleId, logFactory, StartRealProcess, VmConnectLocator.TryGetFullPath)
    {
    }

    /// <summary>
    /// Test-visible constructor: the process factory and the exe locator are seams so the
    /// behavior is verified on machines without the Hyper-V management tools (no real
    /// vmconnect.exe) and without the host machine's state changing the outcome.
    /// </summary>
    public VmConnectExternalSession(
        Guid vmId,
        string vmName,
        string vmConsoleId,
        IAppLogFactory logFactory,
        Func<ProcessStartInfo, IExternalConsoleProcess> processFactory,
        Func<string?>? exeLocator = null)
    {
        VmId = vmId;
        VmName = vmName;
        _vmConsoleId = vmConsoleId;
        _log = logFactory.GetLogger("Rdp-console:" + vmName);
        _processFactory = processFactory;
        _exeLocator = exeLocator ?? VmConnectLocator.TryGetFullPath;
        HostMode = SessionHostMode.Standalone;
    }

    /// <summary>
    /// vmconnect's positional argument list: "server vmId", or just "vmId" when no server is
    /// given (the local Hyper-V host is then implied). Built via ArgumentList so neither token
    /// can split or inject further arguments.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string? server, string vmId) =>
        string.IsNullOrWhiteSpace(server) ? new[] { vmId } : new[] { server, vmId };

    public Guid VmId { get; }
    public string VmName { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public SessionHostMode HostMode { get; set; }
    public object? HostControl => null;

    /// <summary>No embedded control: vmconnect owns its console window; nothing is redirectable.</summary>
    public SessionCapabilities Capabilities { get; } = new()
    {
        ExternalClient = true,
        ClipboardSupported = false,
        FileClipboardSupported = false,
        DriveRedirectionSupported = false,
        SmartSizingSupported = false,
        MultiMonitorSupported = false,
        AudioRedirectionSupported = false,
        PrinterRedirectionSupported = false,
        GatewaySupported = false,
        CtrlAltDelSupported = false,
    };

    public string? LastError { get; private set; }
    public string? TechnicalError { get; private set; }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SessionErrorEventArgs>? SessionError;

    /// <summary>No control to prepare; the console window is created at dial time.</summary>
    public void PrepareControl()
    {
    }

    /// <summary>Launches vmconnect.exe once (idempotent). Failures flip State to Failed without throwing.</summary>
    public void StartConnect()
    {
        IExternalConsoleProcess? process = null;
        Exception? startError = null;
        string? missing = null;

        lock (_gate)
        {
            if (_disposed || _started)
            {
                return;
            }

            var exe = _exeLocator();
            if (exe is null)
            {
                missing =
                    "The Hyper-V console client (vmconnect.exe) was not found on this PC. " +
                    "Install the Hyper-V management tools, or set the host in Edit to connect over RDP.";
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                };
                foreach (var argument in BuildArguments(server: null, _vmConsoleId))
                {
                    startInfo.ArgumentList.Add(argument);
                }

                try
                {
                    process = _processFactory(startInfo);
                    process.Exited += OnProcessExited;
                    _started = true;
                    _process = process;
                }
                catch (Exception ex)
                {
                    startError = ex;
                }
            }
        }

        if (missing is not null)
        {
            Fail(missing, missing);
            return;
        }

        if (startError is not null)
        {
            Fail("Could not start the Hyper-V console client.", startError.Message);
            _log.Error("vmconnect.exe start failed: " + startError.Message, startError);
            return;
        }

        Transition(ConnectionState.Connecting);
    }

    /// <summary>
    /// Resolves to Connected as soon as the console process is confirmed alive: vmconnect shows
    /// its own window and owns the session, so there is no handshake observable here. Never
    /// throws — a dead or failed process flips State so the orchestrator can report it.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (State == ConnectionState.Connected)
        {
            return Task.CompletedTask;
        }

        StartConnect();

        IExternalConsoleProcess? process;
        lock (_gate)
        {
            process = _started ? _process : null;
        }

        if (process is null)
        {
            // StartConnect already recorded Failed; return quietly for the orchestrator.
            return Task.CompletedTask;
        }

        // Check-and-transition under one gate (same race gate as MstscExternalSession):
        // a console that dies between the liveness check and the Connected transition must
        // never strand the session as "Connected" with a dead handle.
        var racedAway = false;
        lock (_gate)
        {
            if (process.HasExited || _exited)
            {
                racedAway = true;
            }
            else
            {
                Transition(ConnectionState.Connected);
            }
        }

        if (racedAway)
        {
            Fail("The Hyper-V console window closed before the session opened.", null);
        }

        return Task.CompletedTask;
    }

    /// <summary>Relaunches the console when the previous window has gone; no-op while one is alive.</summary>
    public Task ReconnectAsync()
    {
        ThrowIfDisposed();
        IExternalConsoleProcess? staleProcess;
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return Task.CompletedTask;
            }

            // Detach the dead handle's handler BEFORE clearing _exited: a late exit event
            // from the old process must never poison the next attempt.
            staleProcess = _process;
            _started = false;
            _exited = false;
            _disconnecting = false;
        }

        if (staleProcess is not null)
        {
            staleProcess.Exited -= OnProcessExited;
        }

        StartConnect();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        ThrowIfDisposed();
        IExternalConsoleProcess? process;
        lock (_gate)
        {
            process = _process;
            _disconnecting = true;
        }

        Transition(ConnectionState.Disconnecting);
        if (process is { HasExited: false })
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex)
            {
                _log.Warn("vmconnect.exe did not close cleanly: " + ex.Message);
            }
        }

        Transition(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public void SendCtrlAltDel()
    {
        // The console window handles its own secure-attention sequence.
    }

    public void SetDisplayMode(DisplayScaleMode mode)
    {
    }

    public void SetSmartSizing(bool enabled)
    {
    }

    public void ToggleFullscreen()
    {
    }

    public void SetClipboardRedirect(bool enabled)
    {
    }

    public void SetAudioRedirect(bool enabled)
    {
    }

    public void SetDriveRedirection(bool enabled, string drives)
    {
    }

    public void Activate()
    {
        // vmconnect owns its window; bringing it forward is left to the OS taskbar.
    }

    public IReadOnlyList<string> GetRemoteClipboardFiles() => Array.Empty<string>();

    public ValueTask DisposeAsync()
    {
        IExternalConsoleProcess? process;
        lock (_gate)
        {
            if (_disposed)
            {
                return default;
            }

            _disposed = true;
            process = _process;
            _process = null;
        }

        if (process is not null)
        {
            process.Exited -= OnProcessExited;
            if (!process.HasExited)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Best effort during teardown.
                }
            }

            process.Dispose();
        }

        return default;
    }

    private static IExternalConsoleProcess StartRealProcess(ProcessStartInfo startInfo) => new ProcessHandle(startInfo);

    private void OnProcessExited(object? sender, EventArgs e)
    {
        bool ignore;
        lock (_gate)
        {
            _exited = true;
            ignore = _disposed || _disconnecting;
        }

        if (ignore)
        {
            return;
        }

        Transition(ConnectionState.Disconnected, "The Hyper-V console window was closed.");
    }

    private void Fail(string friendly, string? technical)
    {
        LastError = friendly;
        TechnicalError = technical;
        SessionError?.Invoke(this, new SessionErrorEventArgs(friendly, technical ?? friendly));
        Transition(ConnectionState.Failed, friendly);
    }

    private void Transition(ConnectionState next, string? message = null)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(next, message));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VmConnectExternalSession));
        }
    }
}
