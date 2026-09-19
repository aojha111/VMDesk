using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Models;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// The orchestrator owns timeout and retry (spec §82-84). These tests pin the
/// behaviours the connect flow relies on: success passes through, the user's
/// cancel token wins, and failure after the final attempt raises
/// VmConnectionException with a friendly message.
/// </summary>
public class ConnectionOrchestratorTests
{
    private static VirtualMachineEntity Vm(
        int timeoutSeconds = 30,
        int retryCount = 1,
        int retryDelaySeconds = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Orchestrated VM",
        Host = "host",
        Port = 3389,
        ConnectionTimeoutSeconds = timeoutSeconds,
        ConnectionRetryCount = retryCount,
        ConnectionRetryDelaySeconds = retryDelaySeconds
    };

    private static ConnectionOrchestrator Create() =>
        new(LibraryReliabilityTests.Logs());

    [Fact]
    public async Task ConnectAsync_passes_through_when_the_session_reaches_connected()
    {
        var orchestrator = Create();
        var session = new FakeSession(Guid.NewGuid(), "vm");
        session.OnConnect = _ =>
        {
            session.ReachConnected();
            return Task.CompletedTask;
        };

        await orchestrator.ConnectAsync(session, Vm(), progress: null, CancellationToken.None);

        Assert.Equal(1, session.ConnectCalls);
    }

    [Fact]
    public async Task ConnectAsync_honours_the_caller_cancel_token_immediately()
    {
        var orchestrator = Create();
        var session = new FakeSession(Guid.NewGuid(), "vm");
        // The fake must honour the token like the real RDP session does.
        session.OnConnect = token => Task.Delay(Timeout.InfiniteTimeSpan, token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orchestrator.ConnectAsync(session, Vm(), progress: null, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ConnectAsync_times_out_when_the_session_never_connects()
    {
        var orchestrator = Create();
        var session = new FakeSession(Guid.NewGuid(), "vm");
        session.OnConnect = token => Task.Delay(Timeout.InfiniteTimeSpan, token); // Hangs until the timeout token fires.

        // The per-VM timeout is clamped to a 5s minimum. A hung dial surfaces as
        // a VmConnectionException carrying the timeout message, which is what
        // the connect UI shows the user.
        var vm = Vm(timeoutSeconds: 5, retryCount: 0);
        var exception = await Assert.ThrowsAsync<VmConnectionException>(
            () => orchestrator.ConnectAsync(session, vm, progress: null, CancellationToken.None));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectAsync_wraps_failure_after_all_attempts_in_VmConnectionException()
    {
        var orchestrator = Create();
        var session = new FakeSession(Guid.NewGuid(), "vm");
        session.OnConnect = _ =>
        {
            session.Fail("dial failed");
            throw new InvalidOperationException("dial failed");
        };

        var vm = Vm(retryCount: 1); // Two attempts total.
        var exception = await Assert.ThrowsAsync<VmConnectionException>(
            () => orchestrator.ConnectAsync(session, vm, progress: null, CancellationToken.None));

        Assert.Equal(2, session.ConnectCalls); // Retried once.
        Assert.Contains("Unable to connect", exception.Message);
    }

    [Fact]
    public async Task ConnectAsync_disconnects_the_session_between_attempts()
    {
        var orchestrator = Create();
        var session = new FakeSession(Guid.NewGuid(), "vm");
        session.OnConnect = _ =>
        {
            session.Fail("first attempt refused");
            throw new InvalidOperationException("refused");
        };

        await Assert.ThrowsAsync<VmConnectionException>(
            () => orchestrator.ConnectAsync(session, Vm(retryCount: 1), progress: null, CancellationToken.None));

        Assert.True(session.DisconnectCalls >= 1); // Pending connect torn down between attempts.
    }
}
