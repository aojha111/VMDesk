using System.Diagnostics;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;

namespace VMDesk.Rdp;

/// <summary>
/// External-session fallback: when the Microsoft RDP ActiveX control cannot be
/// instantiated (Task 1's Probe reports this honestly), the engine launches the
/// real Windows Remote Desktop client (mstsc.exe) as a separate process and this
/// session mirrors its lifecycle. There is no embedded control — HostControl
/// stays null and PrepareControl is a no-op.
///
/// Credentials are a documented limitation: passwords are NEVER placed on the
/// command line, so mstsc.exe prompts the user itself. The first launch logs an
/// Info note about this.
/// </summary>
public sealed class MstscExternalSession : IRemoteSession
{
    /// <summary>Process seam so the session lifecycle is unit-testable without spawning mstsc.</summary>
    internal interface IExternalRdpProcess : IDisposable
    {
        bool HasExited { get; }
        event EventHandler? Exited;
        void Kill();
    }

    /// <summary>Real implementation over System.Diagnostics.Process.</summary>
    private sealed class MstscProcessHandle : IExternalRdpProcess
    {
        private readonly Process _process;

        public MstscProcessHandle(ProcessStartInfo startInfo)
        {
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("mstsc.exe did not start.");
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? Exited;
        public bool HasExited => _process.HasExited;
        public void Kill() => _process.Kill(entireProcessTree: true);
        public void Dispose() => _process.Dispose();
    }

    private static int _credentialNoticeLogged;

    private readonly object _gate = new();
    private readonly IAppLog _log;
    private readonly string _host;
    private readonly int _port;
    private readonly Func<ProcessStartInfo, IExternalRdpProcess> _processFactory;
    private IExternalRdpProcess? _process;
    private bool _started;
    private bool _exited;
    private bool _disconnecting;
    private bool _disposed;

    public MstscExternalSession(Guid vmId, string vmName, string host, int port, IAppLogFactory logFactory)
        : this(vmId, vmName, host, port, logFactory, StartRealProcess)
    {
    }

    internal MstscExternalSession(
        Guid vmId,
        string vmName,
        string host,
        int port,
        IAppLogFactory logFactory,
        Func<ProcessStartInfo, IExternalRdpProcess> processFactory)
    {
        VmId = vmId;
        VmName = vmName;
        _host = host;
        _port = port;
        _log = logFactory.GetLogger("Rdp-external:" + vmName);
        _processFactory = processFactory;
        HostMode = SessionHostMode.Standalone;
    }

    /// <summary>
    /// mstsc's canonical target switch: /v:host or /v:host:port (port 0 or negative
    /// means the default 3389). Built as a single argument token via
    /// ProcessStartInfo.ArgumentList so a host containing spaces cannot split into
    /// extra argv tokens or inject additional mstsc switches.
    /// </summary>
    public static string BuildTargetArgument(string host, int port) =>
        port > 0 ? "/v:" + host + ":" + port : "/v:" + host;

    public Guid VmId { get; }
    public string VmName { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public SessionHostMode HostMode { get; set; }

    /// <summary>No embedded control: mstsc.exe owns its own window.</summary>
    public object? HostControl => null;

    /// <summary>Nothing inside the session can be controlled from VMDesk; only the external flag is true.</summary>
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

    /// <summary>No control to prepare; the external window is created at dial time.</summary>
    public void PrepareControl()
    {
    }

    /// <summary>Launches mstsc.exe once (idempotent). Failures flip State to Failed without throwing.</summary>
    public void StartConnect()
    {
        IExternalRdpProcess? process = null;
        Exception? startError = null;
        string? missing = null;

        lock (_gate)
        {
            if (_disposed || _started)
            {
                return;
            }

            var exe = MstscLocator.TryGetFullPath();
            if (exe is null)
            {
                missing = "mstsc.exe was not found in this Windows installation.";
            }
            else
            {
                // Known limitation, logged once per app run: the saved password is never
                // placed on the command line, so mstsc.exe prompts the user itself.
                if (Interlocked.Exchange(ref _credentialNoticeLogged, 1) == 0)
                {
                    _log.Info("External mstsc sessions do not receive saved credentials: mstsc.exe will prompt for them. Passwords are never passed on the command line.");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                };

                // ArgumentList, not a concatenated Arguments string: the host is
                // quoted by the OS argument-pasting layer, so spaces or switch-like
                // characters in it cannot inject extra mstsc arguments.
                startInfo.ArgumentList.Add(BuildTargetArgument(_host, _port));

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
            Fail("Could not start the Windows Remote Desktop client.", startError.Message);
            _log.Error("mstsc.exe start failed: " + startError.Message, startError);
            return;
        }

        Transition(ConnectionState.Connecting);
    }

    /// <summary>
    /// Resolves to Connected as soon as the client process is confirmed alive: mstsc
    /// shows its own UI and handles auth, so there is no handshake observable here.
    /// Never throws — a dead or failed process flips State so the orchestrator can
    /// report it, and a user cancel has nothing pending to unwind.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (State == ConnectionState.Connected)
        {
            return Task.CompletedTask;
        }

        StartConnect();

        IExternalRdpProcess? process;
        lock (_gate)
        {
            process = _started ? _process : null;
        }

        if (process is null)
        {
            // StartConnect already recorded Failed; return quietly for the orchestrator.
            return Task.CompletedTask;
        }

        // Check-and-transition under one gate: OnProcessExited sets _exited under the
        // same lock, so a process that dies between the liveness check and the
        // Connected transition can never strand the session as "Connected" with a
        // dead handle (which would also block the manager's stale-session replacement).
        var racedAway = false;
        lock (_gate)
        {
            // HasExited first: if the exit notification lands while we ask, the
            // handler has already set _exited by the time this expression reads it.
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
            Fail("The Windows Remote Desktop client closed before the session opened.", null);
        }

        return Task.CompletedTask;
    }

    /// <summary>Relaunches the client when the previous window has gone; no-op while one is alive.</summary>
    public Task ReconnectAsync()
    {
        ThrowIfDisposed();
        IExternalRdpProcess? staleProcess;
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return Task.CompletedTask;
            }

            // Detach the dead handle's handler BEFORE clearing _exited: a late exit
            // event from the old process must never poison the next attempt.
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
        IExternalRdpProcess? process;
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
                _log.Warn("mstsc.exe did not close cleanly: " + ex.Message);
            }
        }

        Transition(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public void SendCtrlAltDel()
    {
        // The external client exposes its own Ctrl+Alt+End handling; nothing to do here.
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
        // mstsc owns its window; bringing it forward is left to the OS taskbar.
    }

    public IReadOnlyList<string> GetRemoteClipboardFiles() => Array.Empty<string>();

    public ValueTask DisposeAsync()
    {
        IExternalRdpProcess? process;
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

    private static IExternalRdpProcess StartRealProcess(ProcessStartInfo startInfo) => new MstscProcessHandle(startInfo);

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

        Transition(ConnectionState.Disconnected, "The Remote Desktop client window was closed.");
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
            throw new ObjectDisposedException(nameof(MstscExternalSession));
        }
    }
}
