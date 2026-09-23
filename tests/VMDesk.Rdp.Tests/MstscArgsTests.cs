using System.Diagnostics;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Rdp;
using VMDesk.Rdp.Interop;
using Xunit;

namespace VMDesk.Rdp.Tests;

/// <summary>
/// The mstsc.exe fallback must build the right target argument, launch exactly once
/// through an injectable process seam (never spawning real processes here), resolve
/// to Connected when the client is alive, and follow the process exit to Disconnected.
/// </summary>
public class MstscArgsTests
{
    /// <summary>Records starts and lets the test drive Exited/Kill without a real process.</summary>
    private sealed class FakeExternalProcess : MstscExternalSession.IExternalRdpProcess
    {
        private bool _hasExited;
        private bool _raceExitAnnounced;

        public List<ProcessStartInfo> Starts { get; } = new();
        public int KillCount { get; private set; }
        public bool Disposed { get; private set; }
        public Func<ProcessStartInfo, MstscExternalSession.IExternalRdpProcess>? ThrowOnStart { get; set; }
        public event EventHandler? Exited;

        /// <summary>
        /// When true, the first HasExited read reports "alive" but synchronously
        /// announces the exit through the Exited event right after being asked —
        /// the exact race where the process dies between the caller's liveness
        /// check and its Connected transition. Later reads report exited.
        /// </summary>
        public bool RaceExitDuringCheck { get; set; }

        public bool HasExited
        {
            get
            {
                if (RaceExitDuringCheck && !_raceExitAnnounced)
                {
                    _raceExitAnnounced = true;
                    Exited?.Invoke(this, EventArgs.Empty);
                    return false;
                }

                return _hasExited || (RaceExitDuringCheck && _raceExitAnnounced);
            }
            set => _hasExited = value;
        }

        public MstscExternalSession.IExternalRdpProcess Start(ProcessStartInfo psi)
        {
            Starts.Add(psi);
            if (ThrowOnStart is not null)
            {
                return ThrowOnStart(psi);
            }

            return this;
        }

        public void RaiseExited() => Exited?.Invoke(this, EventArgs.Empty);
        public void Kill() => KillCount++;
        public void Dispose() => Disposed = true;
    }

    private static MstscExternalSession CreateSession(FakeExternalProcess process) =>
        new(Guid.NewGuid(), "external VM", "10.0.0.5", 3390, new NullLogFactory(), process.Start);

    public static TheoryData<string, int, string> Cases() => new()
    {
        { "10.0.0.5", 0, "/v:10.0.0.5" },
        { "10.0.0.5", 3390, "/v:10.0.0.5:3390" },
        { "host.example", -1, "/v:host.example" },
        { "SERVER01", 3389, "/v:SERVER01:3389" },
        { "win 11 vm", 3390, "/v:win 11 vm:3390" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Builds_canonical_v_target_argument(string host, int port, string expected) =>
        Assert.Equal(expected, MstscExternalSession.BuildTargetArgument(host, port));

    [Fact]
    public void Locator_finds_system32_mstsc() => Assert.NotNull(MstscLocator.TryGetFullPath());

    [Fact]
    public void PrepareControl_is_a_noop_and_never_exposes_a_host_control()
    {
        var session = CreateSession(new FakeExternalProcess());

        session.PrepareControl();

        Assert.Null(session.HostControl);
    }

    [Fact]
    public async Task ConnectAsync_launches_mstsc_once_and_resolves_connected()
    {
        var process = new FakeExternalProcess();
        var session = CreateSession(process);
        var states = new List<ConnectionState>();
        session.StateChanged += (_, e) => states.Add(e.State);

        await session.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, session.State);
        Assert.Single(process.Starts);
        Assert.Equal("/v:10.0.0.5:3390", Assert.Single(process.Starts[0].ArgumentList));
        Assert.True(string.IsNullOrEmpty(process.Starts[0].Arguments));
        Assert.True(process.Starts[0].UseShellExecute);
        Assert.Contains(Path.GetFileName(MstscLocator.TryGetFullPath()!), process.Starts[0].FileName);
        Assert.Null(session.LastError);
        Assert.Contains(ConnectionState.Connecting, states);
        Assert.Equal(ConnectionState.Connected, states[^1]);
    }

    [Fact]
    public async Task A_host_with_spaces_reaches_mstsc_as_exactly_one_argument_token()
    {
        var process = new FakeExternalProcess();
        var session = new MstscExternalSession(
            Guid.NewGuid(), "spaced VM", "win 11 host", 3390, new NullLogFactory(), process.Start);

        await session.ConnectAsync(CancellationToken.None);

        var psi = Assert.Single(process.Starts);
        Assert.Equal("/v:win 11 host:3390", Assert.Single(psi.ArgumentList));
        Assert.True(string.IsNullOrEmpty(psi.Arguments));
    }

    [Fact]
    public async Task Process_exiting_during_the_connected_check_never_strands_as_connected()
    {
        var process = new FakeExternalProcess { RaceExitDuringCheck = true };
        var session = CreateSession(process);

        await session.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Failed, session.State);
        Assert.NotNull(session.LastError);
    }

    [Fact]
    public async Task ConnectAsync_is_idempotent_and_never_relaunches()
    {
        var process = new FakeExternalProcess();
        var session = CreateSession(process);

        await session.ConnectAsync(CancellationToken.None);
        await session.ConnectAsync(CancellationToken.None);

        Assert.Single(process.Starts);
        Assert.Equal(ConnectionState.Connected, session.State);
    }

    [Fact]
    public async Task Process_exit_flips_a_live_session_to_disconnected_once()
    {
        var process = new FakeExternalProcess();
        var session = CreateSession(process);
        await session.ConnectAsync(CancellationToken.None);
        var states = new List<ConnectionState>();
        session.StateChanged += (_, e) => states.Add(e.State);

        process.HasExited = true;
        process.RaiseExited();

        Assert.Equal(ConnectionState.Disconnected, session.State);
        Assert.Single(states);
        Assert.Equal(ConnectionState.Disconnected, states[0]);
    }

    [Fact]
    public async Task Exit_before_connect_completes_never_reports_connected()
    {
        var process = new FakeExternalProcess();
        var session = CreateSession(process);

        process.HasExited = true;
        await session.ConnectAsync(CancellationToken.None);

        Assert.NotEqual(ConnectionState.Connected, session.State);
        Assert.NotNull(session.LastError);
    }

    [Fact]
    public async Task DisconnectAsync_kills_the_client_process()
    {
        var process = new FakeExternalProcess();
        var session = CreateSession(process);
        await session.ConnectAsync(CancellationToken.None);

        await session.DisconnectAsync();

        Assert.Equal(1, process.KillCount);
        Assert.Equal(ConnectionState.Disconnected, session.State);
    }

    [Fact]
    public async Task A_failed_start_surfaces_a_friendly_error_without_throwing()
    {
        var process = new FakeExternalProcess { ThrowOnStart = _ => throw new InvalidOperationException("no mstsc") };
        var session = CreateSession(process);

        await session.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Failed, session.State);
        Assert.Contains("Remote Desktop", session.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mstsc", session.TechnicalError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisposeAsync_kills_and_disposes_the_process()
    {
        var process = new FakeExternalProcess();
        var session = CreateSession(process);
        await session.ConnectAsync(CancellationToken.None);

        await session.DisposeAsync();

        Assert.Equal(1, process.KillCount);
        Assert.True(process.Disposed);
    }

    [Fact]
    public void Capabilities_report_external_client_only()
    {
        var session = CreateSession(new FakeExternalProcess());
        var caps = session.Capabilities;

        Assert.True(caps.ExternalClient);
        Assert.False(caps.ClipboardSupported);
        Assert.False(caps.FileClipboardSupported);
        Assert.False(caps.DriveRedirectionSupported);
        Assert.False(caps.SmartSizingSupported);
        Assert.False(caps.MultiMonitorSupported);
        Assert.False(caps.AudioRedirectionSupported);
        Assert.False(caps.PrinterRedirectionSupported);
        Assert.False(caps.GatewaySupported);
        Assert.False(caps.CtrlAltDelSupported);
    }

    [Fact]
    public void HostMode_defaults_to_standalone_for_external_sessions()
    {
        var session = CreateSession(new FakeExternalProcess());

        Assert.Equal(SessionHostMode.Standalone, session.HostMode);
    }

    [Fact]
    public void Control_commands_are_safe_noops()
    {
        var session = CreateSession(new FakeExternalProcess());

        session.SendCtrlAltDel();
        session.SetDisplayMode(DisplayScaleMode.Fullscreen);
        session.SetSmartSizing(true);
        session.ToggleFullscreen();
        session.SetClipboardRedirect(false);
        session.SetAudioRedirect(false);
        session.SetDriveRedirection(true, "C");
        session.Activate();

        Assert.Empty(session.GetRemoteClipboardFiles());
    }
}
