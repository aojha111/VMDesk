using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// Catalog reconciliation for discovered VMs: add/update/stale cycle, manual rows untouched,
/// user credentials and notes preserved (Task 5 spec).
/// </summary>
public sealed class DiscoverySyncTests
{
    /// <summary>In-memory IVmRepository; rows keep identity so mutations are observable.</summary>
    private sealed class InMemoryVmRepository : IVmRepository
    {
        public List<VirtualMachineEntity> Store { get; } = new();
        public int AddCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<IReadOnlyList<VirtualMachineEntity>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(Store.ToList());

        public Task<VirtualMachineEntity?> GetAsync(Guid id) =>
            Task.FromResult(Store.FirstOrDefault(v => v.Id == id));

        public Task<VirtualMachineEntity> AddAsync(VirtualMachineEntity vm)
        {
            AddCalls++;
            Store.Add(vm);
            return Task.FromResult(vm);
        }

        public Task UpdateAsync(VirtualMachineEntity vm)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id) => throw new NotSupportedException("Discovery sync must never delete rows.");
        public Task<IReadOnlyList<GroupEntity>> GetGroupsAsync() => throw new NotSupportedException();
        public Task<GroupEntity> AddGroupAsync(string name) => throw new NotSupportedException();
        public Task UpdateGroupAsync(GroupEntity group) => throw new NotSupportedException();
        public Task DeleteGroupAsync(Guid id) => throw new NotSupportedException();
        public Task<IReadOnlyList<TagEntity>> GetTagsAsync() => throw new NotSupportedException();
        public Task SetTagsAsync(Guid vmId, IReadOnlyList<string> tags) => throw new NotSupportedException();
        public Task SetFavoriteAsync(Guid id, bool favorite) => throw new NotSupportedException();
        public Task RecordConnectionAsync(Guid id, bool success, string? error) => throw new NotSupportedException();
        public Task<IReadOnlyList<RecentConnectionEntity>> GetRecentConnectionsAsync(int count) => throw new NotSupportedException();
        public Task ClearRecentConnectionsAsync() => throw new NotSupportedException();
    }

    private static (DiscoverySyncService Service, InMemoryVmRepository Repository) Create()
    {
        var repository = new InMemoryVmRepository();
        return (new DiscoverySyncService(repository, LibraryReliabilityTests.Logs()), repository);
    }

    [Fact]
    public async Task Sync_adds_then_updates_then_marks_disappeared_vms_stale()
    {
        var (service, repository) = Create();

        var report = await service.SyncAsync(new[]
        {
            new DiscoveredVm("HyperV", "vm-1", "Dev Box", "10.0.0.5", "Running")
        });

        Assert.Equal(new SyncReport(Added: 1, Updated: 0, StaleMarked: 0), report);
        var row = Assert.Single(repository.Store);
        Assert.Equal("HyperV", row.Provider);
        Assert.Equal("vm-1", row.ProviderId);
        Assert.Equal("Dev Box", row.Name);
        Assert.Equal("10.0.0.5", row.Host);
        Assert.Equal("Running", row.PowerState);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.False(string.IsNullOrEmpty(row.CredentialReference));
        var addedId = row.Id;
        var credentialReference = row.CredentialReference;

        report = await service.SyncAsync(new[]
        {
            new DiscoveredVm("HyperV", "vm-1", "Dev Box Renamed", "10.0.0.5", "Off")
        });

        Assert.Equal(new SyncReport(0, 1, 0), report);
        row = Assert.Single(repository.Store);
        Assert.Equal(addedId, row.Id); // matched on (Provider, ProviderId) — no duplicate row
        Assert.Equal("Dev Box Renamed", row.Name);
        Assert.Equal("Off", row.PowerState);
        Assert.Equal(credentialReference, row.CredentialReference);

        // Identical scan data changes nothing at all.
        report = await service.SyncAsync(new[]
        {
            new DiscoveredVm("HyperV", "vm-1", "Dev Box Renamed", "10.0.0.5", "Off")
        });
        Assert.Equal(new SyncReport(0, 0, 0), report);

        // The provider no longer reports the VM: the row is kept but marked Unknown.
        report = await service.SyncAsync(Array.Empty<DiscoveredVm>());

        Assert.Equal(new SyncReport(0, 0, 1), report);
        row = Assert.Single(repository.Store);
        Assert.Equal("Unknown", row.PowerState);

        // Already Unknown: stale marking is not re-counted on the next scan.
        report = await service.SyncAsync(Array.Empty<DiscoveredVm>());
        Assert.Equal(new SyncReport(0, 0, 0), report);
    }

    [Fact]
    public async Task Sync_never_touches_manual_rows()
    {
        var (service, repository) = Create();
        var manual = new VirtualMachineEntity
        {
            Id = Guid.NewGuid(),
            Name = "Manual Box",
            Host = "10.0.0.5",
            PowerState = "Running",
            CredentialReference = "VMDesk/manual"
        };
        Assert.Equal("Manual", manual.Provider); // entity default
        repository.Store.Add(manual);

        var report = await service.SyncAsync(new[]
        {
            new DiscoveredVm("HyperV", "vm-1", "Manual Box", "10.0.0.5", "Running")
        });

        // Same host and name as the manual row, but matching is on (Provider, ProviderId):
        // the discovered VM is added as its own row and the manual row stays untouched.
        Assert.Equal(new SyncReport(1, 0, 0), report);
        Assert.Equal(2, repository.Store.Count);
        Assert.Equal("Manual Box", manual.Name);
        Assert.Equal("10.0.0.5", manual.Host);
        Assert.Equal("Running", manual.PowerState);
        Assert.Equal("VMDesk/manual", manual.CredentialReference);

        report = await service.SyncAsync(Array.Empty<DiscoveredVm>());

        Assert.Equal(new SyncReport(0, 0, 1), report); // only the discovered row is marked stale
        Assert.Equal("Running", manual.PowerState);
    }

    [Fact]
    public async Task Resync_preserves_user_set_credentials_notes_and_host()
    {
        var (service, repository) = Create();
        await service.SyncAsync(new[] { new DiscoveredVm("Libvirt", "dom-9", "Win11", null, "Running") });
        var row = Assert.Single(repository.Store);
        Assert.Equal(string.Empty, row.Host); // null host normalises to empty

        row.CredentialReference = "VMDesk/user-set";
        row.Notes = "keep me";

        var report = await service.SyncAsync(new[] { new DiscoveredVm("Libvirt", "dom-9", "Win11-2", null, "Paused") });

        Assert.Equal(new SyncReport(0, 1, 0), report);
        row = Assert.Single(repository.Store);
        Assert.Equal("Win11-2", row.Name);
        Assert.Equal("Paused", row.PowerState);
        Assert.Equal("VMDesk/user-set", row.CredentialReference);
        Assert.Equal("keep me", row.Notes);
        Assert.Equal(string.Empty, row.Host); // a null scan host never wipes the stored value
    }

    [Fact]
    public async Task A_vanished_provider_only_stale_marks_its_own_rows()
    {
        var (service, repository) = Create();
        await service.SyncAsync(new[]
        {
            new DiscoveredVm("HyperV", "vm-a", "Alpha", "h-a", "Running"),
            new DiscoveredVm("Libvirt", "dom-b", "Beta", "h-b", "Running")
        });
        Assert.Equal(2, repository.Store.Count);

        // Only the HyperV provider reports in this scan.
        var report = await service.SyncAsync(new[]
        {
            new DiscoveredVm("HyperV", "vm-a", "Alpha", "h-a", "Running")
        });

        Assert.Equal(new SyncReport(0, 0, 1), report);
        var hyperV = repository.Store.Single(v => v.Provider == "HyperV");
        var libvirt = repository.Store.Single(v => v.Provider == "Libvirt");
        Assert.Equal("Running", hyperV.PowerState);
        Assert.Equal("Unknown", libvirt.PowerState);
        Assert.Equal(2, repository.Store.Count); // nothing deleted
    }

    [Fact]
    public async Task Sync_never_deletes_rows()
    {
        var (service, repository) = Create();
        await service.SyncAsync(new[] { new DiscoveredVm("HyperV", "vm-1", "Dev", "h", "Running") });

        await service.SyncAsync(Array.Empty<DiscoveredVm>());
        await service.SyncAsync(Array.Empty<DiscoveredVm>());

        Assert.Single(repository.Store); // the row survives repeated empty scans
    }
}
