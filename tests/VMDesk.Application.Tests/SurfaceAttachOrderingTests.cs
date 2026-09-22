using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// Regression guard for the standalone parent-before-connect ordering on a real WPF
/// dispatcher: Window.Show() surfaces the window but Loaded is a dispatcher event
/// queued at DispatcherPriority.Loaded — relying on it having drained before the
/// dial is luck, not construction. The surfaceReady callback must pump the
/// dispatcher to that priority before returning, otherwise the orchestrator can
/// dial while the prepared control is still unparented — the exact
/// re-parenting-kills-the-handshake hazard. Mirrors the Show +
/// Dispatcher.Yield(Loaded) pattern in MainWindow's surfaceReady callback.
/// </summary>
public class SurfaceAttachOrderingTests
{
    [Fact]
    public async Task YieldToLoaded_guarantees_the_control_is_parented_before_the_dial()
    {
        var fake = await StaWpf.RunAsync(async () =>
        {
            var session = new SurfaceSession();
            var window = new AttachingWindow(session);
            var manager = new RemoteSessionManager(
                new FakeEngine((_, _) => session),
                new FakeOrchestrator
                {
                    OnConnect = (s, vm, token) =>
                    {
                        session.AttachedBeforeDial = window.ControlAttached;
                        return s.ConnectAsync(token);
                    },
                },
                LibraryReliabilityTests.Logs());

            await manager.ConnectAsync(
                new VirtualMachineEntity { Name = "x", Host = "h" },
                surfaceReady: async s =>
                {
                    window.Show();
                    // Whether Loaded has already fired by the time Show() returns is a
                    // WPF implementation detail: the event is queued at
                    // DispatcherPriority.Loaded and draining that queue during Show is
                    // luck, not construction (the prior report claimed it was
                    // guaranteed; the review corrected it). Pumping to Loaded priority
                    // is what makes the attach deterministic before the dial starts.
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                    Assert.True(window.ControlAttached);
                });

            window.Close();
            return session;
        });

        // The dial ran only after OnLoaded parenting, and the attach is the
        // authoritative HostMode assignment point (mirrors SessionWindow.OnLoaded).
        Assert.True(fake.AttachedBeforeDial);
        Assert.Equal(new[] { "prepare", "connect" }, fake.Calls.ToArray());
    }

    /// <summary>Window that parents a control in its Loaded handler, like SessionWindow.OnLoaded.</summary>
    private sealed class AttachingWindow : Window
    {
        private readonly SurfaceSession _session;

        public bool ControlAttached { get; private set; }

        public AttachingWindow(SurfaceSession session)
        {
            _session = session;
            Loaded += (_, _) =>
            {
                if (_session.HostControl is System.Windows.Forms.Control control)
                {
                    Content = new WindowsFormsHost { Child = control };
                    ControlAttached = true;
                }
            };
        }
    }

    /// <summary>Session fake whose "connect" records whether the control was already parented.</summary>
    private sealed class SurfaceSession : IRemoteSession
    {
        private readonly System.Windows.Forms.Control _control = new();

        public List<string> Calls { get; } = new();
        public bool AttachedBeforeDial { get; set; }
        public Guid VmId { get; } = Guid.NewGuid();
        public string VmName => "x";
        public ConnectionState State { get; set; } = ConnectionState.Disconnected;
        public SessionHostMode HostMode { get; set; }
        public object? HostControl => _control;
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
        public void Activate() { }
        public IReadOnlyList<string> GetRemoteClipboardFiles() => Array.Empty<string>();
        public ValueTask DisposeAsync()
        {
            _control.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Runs async work on a dedicated STA thread with a pumping dispatcher frame.</summary>
    private static class StaWpf
    {
        public static Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var frame = new DispatcherFrame();
                dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        tcs.TrySetResult(await work());
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                    finally
                    {
                        frame.Continue = false;
                    }
                });
                Dispatcher.PushFrame(frame);
                dispatcher.InvokeShutdown();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }
    }
}
