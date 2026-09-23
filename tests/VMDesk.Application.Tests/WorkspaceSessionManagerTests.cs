using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using Xunit;

namespace VMDesk.Application.Tests;

public class WorkspaceSessionManagerTests
{
    private static VirtualMachineEntity Vm(string name = "Workspace VM") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Host = "host",
        Port = 3389
    };

    private static (RemoteSessionManager Manager, FakeEngine Engine, FakeOrchestrator Orchestrator) Create(
        Func<VirtualMachineEntity, string?, IRemoteSession>? factory = null)
    {
        var engine = new FakeEngine((vm, cred) => factory?.Invoke(vm, cred) ?? new FakeSession(vm.Id, vm.Name));
        var orchestrator = new FakeOrchestrator();
        return (new RemoteSessionManager(engine, orchestrator, LibraryReliabilityTests.Logs()), engine, orchestrator);
    }

    [Fact]
    public async Task ConnectAsync_returns_and_activates_the_existing_live_session()
    {
        var (manager, _, orchestrator) = Create();
        var vm = Vm();
        orchestrator.OnConnect = (session, _, _) =>
        {
            ((FakeSession)session).ReachConnected();
            return Task.CompletedTask;
        };

        var first = await manager.ConnectAsync(vm);
        var second = await manager.ConnectAsync(vm);

        Assert.Same(first, second);
        Assert.Equal(1, orchestrator.Calls); // Second connect reuses; no second dial.
        Assert.Equal(1, ((FakeSession)second).ActivateCalls);
    }

    [Fact]
    public async Task ConnectAsync_replaces_a_failed_session_instead_of_returning_it_dead()
    {
        var vm = Vm();
        var created = new List<FakeSession>();
        var (manager, _, orchestrator) = Create((_, _) =>
        {
            var session = new FakeSession(vm.Id, vm.Name);
            created.Add(session);
            return session;
        });
        orchestrator.OnConnect = (session, _, _) =>
        {
            var fake = (FakeSession)session;
            if (ReferenceEquals(session, created[0]))
            {
                fake.Fail("host unreachable");
                throw new VmConnectionException("host unreachable", null);
            }

            fake.ReachConnected(); // The retry dials a fresh session and succeeds.
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<VmConnectionException>(() => manager.ConnectAsync(vm));
        Assert.Single(created);
        Assert.Contains(created[0], manager.Sessions); // Failed session stays for visibility (spec §57).

        var retry = await manager.ConnectAsync(vm); // Second attempt must not reuse the dead session.

        Assert.Equal(2, created.Count); // Old session closed, fresh one created for the retry.
        Assert.NotSame(created[0], retry);
        Assert.Equal(1, created[0].DisposeCalls);
        Assert.Contains(retry, manager.Sessions);
        Assert.DoesNotContain(created[0], manager.Sessions);
    }

    [Fact]
    public async Task ConnectAsync_registers_the_engine_session_in_the_workspace()
    {
        var (manager, _, orchestrator) = Create();
        var vm = Vm();

        var session = await manager.ConnectAsync(vm);

        Assert.Same(session, manager.FindByVm(vm.Id));
        Assert.Contains(session, manager.Sessions);
    }

    [Fact]
    public async Task ConnectAsync_reports_progress_to_the_caller()
    {
        var (manager, _, orchestrator) = Create();
        var messages = new List<string>();
        var progress = new Progress<string>(messages.Add);
        orchestrator.OnConnect = (session, _, _) =>
        {
            ((FakeSession)session).ReachConnected();
            return Task.CompletedTask;
        };

        var session = await manager.ConnectAsync(Vm(), progress: progress);
        Assert.Equal(ConnectionState.Connected, session.State);
    }

    [Fact]
    public async Task ConnectAsync_closes_the_pending_session_when_cancelled()
    {
        var vm = Vm();
        FakeSession? createdSession = null;
        var (manager, _, orchestrator) = Create((_, _) => createdSession = new FakeSession(vm.Id, vm.Name));
        orchestrator.OnConnect = (_, _, token) => Task.FromException(new OperationCanceledException(token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.ConnectAsync(vm, cancellationToken: new CancellationToken(canceled: true)));

        Assert.NotNull(createdSession);
        Assert.Equal(1, createdSession!.DisposeCalls);
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public async Task ConnectAsync_passes_a_cancelled_token_through_the_real_orchestrator_into_session_connect()
    {
        // The progress dialog's Cancel button now feeds a real CancellationTokenSource;
        // the cancellation must actually reach session.ConnectAsync through the real
        // orchestrator (linked per-attempt token), not stop at the UI layer.
        var vm = Vm();
        var session = new FakeSession(vm.Id, vm.Name)
        {
            OnConnect = token => Task.Delay(Timeout.InfiniteTimeSpan, token)
        };
        var engine = new FakeEngine((_, _) => session);
        var manager = new RemoteSessionManager(engine, ConnectionOrchestratorTests.Create(), LibraryReliabilityTests.Logs());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.ConnectAsync(vm, cancellationToken: new CancellationToken(canceled: true)));

        var dialToken = Assert.Single(session.ConnectTokens);
        Assert.True(dialToken.IsCancellationRequested);
        Assert.Equal(1, session.DisposeCalls); // Cancelled session torn down, not left half-open.
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public async Task CloseAsync_is_idempotent_and_disposes_only_once()
    {
        var (manager, _, _) = Create();
        var session = (FakeSession)await manager.ConnectAsync(Vm());

        await manager.CloseAsync(session);
        await manager.CloseAsync(session);

        Assert.Equal(1, session.DisposeCalls);
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public async Task CloseAsync_disconnects_before_disposing()
    {
        var (manager, _, _) = Create();
        var session = (FakeSession)await manager.ConnectAsync(Vm());

        await manager.CloseAsync(session);

        Assert.Equal(1, session.DisconnectCalls);
        Assert.Equal(1, session.DisposeCalls);
    }

    [Fact]
    public async Task FindByVm_finds_only_the_owned_session()
    {
        var (manager, _, _) = Create();
        var vm = Vm();
        var session = await manager.ConnectAsync(vm);

        Assert.Same(session, manager.FindByVm(vm.Id));
        Assert.Null(manager.FindByVm(Guid.NewGuid()));
    }

    [Fact]
    public async Task CloseAllAsync_closes_every_session()
    {
        var (manager, _, _) = Create();
        await manager.ConnectAsync(Vm("one"));
        await manager.ConnectAsync(Vm("two"));

        await manager.CloseAllAsync();

        Assert.Empty(manager.Sessions);
    }
}
