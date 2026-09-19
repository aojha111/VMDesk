using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// Regression guards for the VM connected view: the recorded connection
/// outcome drives tile/list status and the Recent scope (spec §16).
/// </summary>
public class VmConnectGuardTests
{
    private static (MainViewModel Model, List<VirtualMachineEntity> Store, List<Exception> Errors) Create(params VirtualMachineEntity[] seed)
    {
        var store = seed.ToList();
        var errors = new List<Exception>();
        var repository = ServiceStub.Create<IVmRepository>((method, args) =>
        {
            switch (method.Name)
            {
                case nameof(IVmRepository.GetAllAsync):
                    return Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(store.ToList());
                case nameof(IVmRepository.UpdateAsync):
                    return Task.CompletedTask;
                case nameof(IVmRepository.RecordConnectionAsync):
                    return Task.CompletedTask;
                default:
                    return Task.FromResult<object?>(null)!;
            }
        });
        var settings = ServiceStub.Create<ISettingsService>((_, _) => Task.FromResult(LibraryViewMode.Tile));
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) =>
            Task.FromResult<IReadOnlyList<VMDesk.Core.Models.SavedCredential>>(Array.Empty<VMDesk.Core.Models.SavedCredential>()));
        var model = new MainViewModel(new VmCatalogService(repository, credentials, LibraryReliabilityTests.Logs()), credentials, settings);
        model.ActionFailed += (_, ex) => errors.Add(ex);
        return (model, store, errors);
    }

    [Fact]
    public async Task Successful_connection_marks_the_tile_connected_and_recent()
    {
        var vm = new VirtualMachineEntity { Id = Guid.NewGuid(), Name = "Web", LastConnectionStatus = "Disconnected" };
        var (model, _, _) = Create(vm);
        await model.LoadAsync();

        await model.RecordConnectionAsync(vm, success: true, error: null);

        var tile = Assert.Single(model.Vms);
        Assert.Equal(ConnectionState.Connected, tile.State);
        Assert.NotNull(vm.LastConnectedAt);
        Assert.Equal(string.Empty, vm.LastConnectionError);
    }

    [Fact]
    public async Task Failed_connection_marks_the_tile_failed_without_a_recent_timestamp()
    {
        var vm = new VirtualMachineEntity { Id = Guid.NewGuid(), Name = "Db", LastConnectionStatus = "Disconnected" };
        var (model, _, _) = Create(vm);
        await model.LoadAsync();

        await model.RecordConnectionAsync(vm, success: false, error: "Host unreachable");

        var tile = Assert.Single(model.Vms);
        Assert.Equal(ConnectionState.Failed, tile.State);
        Assert.Null(vm.LastConnectedAt);
        Assert.Equal("Host unreachable", vm.LastConnectionError);
    }

    [Fact]
    public async Task Recording_a_connection_never_breaks_the_ui_when_storage_fails()
    {
        var vm = new VirtualMachineEntity { Id = Guid.NewGuid(), Name = "Flaky" };
        var repository = ServiceStub.Create<IVmRepository>((method, args) => method.Name switch
        {
            nameof(IVmRepository.GetAllAsync) => Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(new[] { vm }),
            nameof(IVmRepository.UpdateAsync) => Task.FromException(new InvalidOperationException("disk full")),
            _ => Task.FromResult<object?>(null)!
        });
        var settings = ServiceStub.Create<ISettingsService>((_, _) => Task.FromResult(LibraryViewMode.Tile));
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) =>
            Task.FromResult<IReadOnlyList<VMDesk.Core.Models.SavedCredential>>(Array.Empty<VMDesk.Core.Models.SavedCredential>()));
        var model = new MainViewModel(new VmCatalogService(repository, credentials, LibraryReliabilityTests.Logs()), credentials, settings);
        var failures = new List<Exception>();
        model.ActionFailed += (_, ex) => failures.Add(ex);

        await model.RecordConnectionAsync(vm, success: true, error: null); // Must not throw.

        Assert.Single(failures);
    }

    [Fact]
    public async Task Recording_skips_unsaved_vms_without_an_id()
    {
        var vm = new VirtualMachineEntity { Id = Guid.Empty, Name = "not yet saved" };
        var (model, _, errors) = Create();

        await model.RecordConnectionAsync(vm, success: true, error: null);

        Assert.Empty(errors);
        Assert.Equal(ConnectionState.Disconnected, vm.State); // Status untouched.
    }

    [Fact]
    public async Task Recent_scope_shows_only_vms_that_have_connected()
    {
        var connected = new VirtualMachineEntity { Id = Guid.NewGuid(), Name = "Live", LastConnectedAt = DateTimeOffset.UtcNow };
        var neverUsed = new VirtualMachineEntity { Id = Guid.NewGuid(), Name = "Idle" };
        var (model, _, _) = Create(connected, neverUsed);
        await model.LoadAsync();
        Assert.Equal(2, model.Vms.Count);

        await model.SetScopeAsync("Recent");

        var recent = Assert.Single(model.Vms);
        Assert.Equal("Live", recent.Name);
    }
}
