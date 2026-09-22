using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// Regression guard for the parent-before-connect ordering bug: the control must be
/// prepared and parented into a visible surface (via surfaceReady) before the
/// orchestrator starts dialing; re-parenting after Connect() kills the handshake.
/// </summary>
public class ParentBeforeConnectTests
{
    private static VirtualMachineEntity Vm() => new()
    {
        Id = Guid.NewGuid(),
        Name = "x",
        Host = "h"
    };

    [Fact]
    public async Task ConnectAsync_prepares_and_surfaces_before_connecting()
    {
        var fake = new RecordingSession();
        var engine = new FakeEngine((_, _) => fake);
        var orchestrator = new FakeOrchestrator { OnConnect = (session, _, token) => session.ConnectAsync(token) };
        var manager = new RemoteSessionManager(engine, orchestrator, LibraryReliabilityTests.Logs());
        manager.SessionAdded += (_, _) => fake.Calls.Add("added");

        var session = await manager.ConnectAsync(Vm(), surfaceReady: s =>
        {
            Assert.NotNull(s); // The surfaced session is the one the caller must parent.
            fake.Calls.Add("surface");
            return Task.CompletedTask;
        });

        Assert.Same(fake, session);
        Assert.Equal(new[] { "prepare", "surface", "added", "connect" }, fake.Calls.ToArray());
    }

    [Fact]
    public async Task ConnectAsync_without_surfaceReady_still_prepares_before_connecting()
    {
        var fake = new RecordingSession();
        var engine = new FakeEngine((_, _) => fake);
        var orchestrator = new FakeOrchestrator { OnConnect = (session, _, token) => session.ConnectAsync(token) };
        var manager = new RemoteSessionManager(engine, orchestrator, LibraryReliabilityTests.Logs());

        await manager.ConnectAsync(Vm());

        Assert.Equal(new[] { "prepare", "connect" }, fake.Calls.ToArray());
    }

    /// <summary>IRemoteSession fake that records the lifecycle call order for assertions.</summary>
    private sealed class RecordingSession : IRemoteSession
    {
        public List<string> Calls { get; } = new();
        public Guid VmId { get; } = Guid.NewGuid();
        public string VmName => "x";
        public ConnectionState State { get; set; } = ConnectionState.Disconnected;
        public SessionHostMode HostMode { get; set; }
        public object? HostControl => null;
        public SessionCapabilities Capabilities { get; } = new();
        public string? LastError { get; }
        public string? TechnicalError { get; }

        public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
#pragma warning disable CS0067 // Never raised: this fake only fails to raise errors because it never has one.
        public event EventHandler<SessionErrorEventArgs>? SessionError;
#pragma warning restore CS0067

        public void PrepareControl() => Calls.Add("prepare");

        public void StartConnect() => Calls.Add("startconnect");

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            Calls.Add("connect");
            State = ConnectionState.Connected;
            StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State));
            return Task.CompletedTask;
        }

        public Task ReconnectAsync() => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public void SendCtrlAltDel() { }
        public void SetDisplayMode(DisplayScaleMode mode) { }
        public void SetSmartSizing(bool enabled) { }
        public void ToggleFullscreen() { }
        public void SetClipboardRedirect(bool enabled) { }
        public void SetAudioRedirect(bool enabled) { }
        public void SetDriveRedirection(bool enabled, string drives) { }
        public void Activate() => Calls.Add("activate");
        public IReadOnlyList<string> GetRemoteClipboardFiles() => Array.Empty<string>();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
