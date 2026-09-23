using System.Diagnostics;
using System.IO;
using System.Reflection;
using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Rdp;
using VMDesk.Rdp.Interop;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// Task 7 UI state: the discovery command must run a scan, put the provider rows into the
/// library (via the Task 5 sync into the same repository the catalog reads), explain in a
/// banner note why nothing was found when no hypervisor exists (the golden path on this
/// dev box), never leave IsDiscovering stuck after a throw, and route a Hyper-V VM without
/// an RDP host to vmconnect.exe with a clean argument list — including when the exe is absent.
/// </summary>
public sealed class DiscoveryUiStateTests
{
    /// <summary>In-memory catalog; only the members the sync and the library load touch are functional.</summary>
    private sealed class RecordingRepository : IVmRepository
    {
        public List<VirtualMachineEntity> Store { get; } = new();
        public Func<VirtualMachineEntity, Task<VirtualMachineEntity>>? OnAdd { get; set; }

        public Task<IReadOnlyList<VirtualMachineEntity>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(Store.ToList());

        public Task<VirtualMachineEntity?> GetAsync(Guid id) =>
            Task.FromResult(Store.FirstOrDefault(v => v.Id == id));

        public Task<VirtualMachineEntity> AddAsync(VirtualMachineEntity vm)
        {
            if (OnAdd is not null) return OnAdd(vm);
            Store.Add(vm);
            return Task.FromResult(vm);
        }

        public Task UpdateAsync(VirtualMachineEntity vm) => Task.CompletedTask;
        public Task DeleteAsync(Guid id) => throw new NotSupportedException();
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

    private sealed class FakeProvider : IVmDiscoveryProvider
    {
        public FakeProvider(string name) => ProviderName = name;

        public string ProviderName { get; }
        public bool Available { get; set; } = true;
        public string? UnavailableReason { get; set; }
        public Func<CancellationToken, Task<IReadOnlyList<DiscoveredVm>>>? OnDiscover { get; set; }

        public bool IsAvailable()
        {
            if (!Available)
            {
                UnavailableReason ??= $"{ProviderName} is not installed.";
            }

            return Available;
        }

        public Task<IReadOnlyList<DiscoveredVm>> DiscoverAsync(CancellationToken ct) =>
            OnDiscover?.Invoke(ct) ?? Task.FromResult<IReadOnlyList<DiscoveredVm>>(Array.Empty<DiscoveredVm>());
    }

    private static ICredentialStore Credentials() =>
        ServiceStub.Create<ICredentialStore>((_, _) => throw new InvalidOperationException("Credentials must not be accessed."));

    private static ISettingsService Settings() =>
        ServiceStub.Create<ISettingsService>((method, args) => method.Name switch
        {
            nameof(ISettingsService.GetAsync) => Task.FromResult(new VMDesk.Core.Models.AppSettingsModel()),
            nameof(ISettingsService.GetValueAsync) => Task.FromResult((VMDesk.Core.Enums.LibraryViewMode)args![1]!),
            _ => Task.CompletedTask
        });

    private static MainViewModel Create(RecordingRepository repository, params IVmDiscoveryProvider[] providers)
    {
        var logs = LibraryReliabilityTests.Logs();
        var credentials = Credentials();
        var catalog = new VmCatalogService(repository, credentials, logs);
        var discovery = new DiscoveryService(
            providers,
            new DiscoverySyncService(repository, logs),
            logs,
            TimeSpan.FromSeconds(5));
        return new MainViewModel(catalog, credentials, Settings(), discovery);
    }

    [Fact]
    public async Task DiscoverAsync_adds_provider_rows_to_the_library()
    {
        var hyperV = new FakeProvider("HyperV");
        hyperV.OnDiscover = _ => Task.FromResult<IReadOnlyList<DiscoveredVm>>(new[]
        {
            new DiscoveredVm("HyperV", "hv-1", "Win11 Console", null, "Running"),
            new DiscoveredVm("HyperV", "hv-2", "Test VM", "10.0.0.7", "Off"),
        });
        var model = Create(new RecordingRepository(), hyperV);

        await model.DiscoverAsync();

        Assert.Equal(2, model.Vms.Count);
        var running = Assert.Single(model.Vms, vm => vm.Name == "Win11 Console");
        Assert.Equal("HyperV", running.Provider);
        Assert.Equal("Running", running.PowerState);
        Assert.Equal("hv-1", running.ProviderId);
        Assert.Equal("Off", Assert.Single(model.Vms, vm => vm.Name == "Test VM").PowerState);
    }

    [Fact]
    public async Task DiscoveryNote_names_every_unavailable_provider_and_its_reason()
    {
        // The real state of this dev box: no hypervisor at all. The banner must say why
        // the list stayed empty and surface the provider's own note verbatim.
        var hyperV = new FakeProvider("HyperV")
        {
            Available = false,
            UnavailableReason = "Hyper-V WMI needs elevation — run VMDesk as administrator to list Hyper-V VMs.",
        };
        var box = new FakeProvider("VirtualBox") { Available = false, UnavailableReason = "VBoxManage was not found." };
        var model = Create(new RecordingRepository(), hyperV, box);

        await model.DiscoverAsync();

        Assert.Contains("elevation", model.DiscoveryNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hyper-V", model.DiscoveryNote);
        Assert.Contains("VirtualBox", model.DiscoveryNote);
        Assert.Contains("VBoxManage was not found.", model.DiscoveryNote);
    }

    [Fact]
    public async Task DiscoverAsync_leaves_isDiscovering_false_after_a_throw()
    {
        var repository = new RecordingRepository
        {
            OnAdd = _ => throw new InvalidOperationException("database locked"),
        };
        var hyperV = new FakeProvider("HyperV");
        hyperV.OnDiscover = _ => Task.FromResult<IReadOnlyList<DiscoveredVm>>(
            new[] { new DiscoveredVm("HyperV", "hv-1", "Win11", null, "Running") });
        var model = Create(repository, hyperV);

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.DiscoverAsync());

        Assert.False(model.IsDiscovering);
        Assert.True(model.DiscoverCommand.CanExecute(null));
    }

    [Fact]
    public async Task DiscoverAsync_treats_cancellation_as_a_cancel_not_an_error()
    {
        var waiting = new FakeProvider("HyperV");
        waiting.OnDiscover = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return (IReadOnlyList<DiscoveredVm>)Array.Empty<DiscoveredVm>();
        };
        var model = Create(new RecordingRepository(), waiting);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await model.DiscoverAsync(cts.Token);

        Assert.False(model.IsDiscovering);
        Assert.Contains("cancel", model.DiscoveryNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discovered_scope_shows_provider_rows_and_hides_manual_ones()
    {
        var repository = new RecordingRepository();
        repository.Store.Add(new VirtualMachineEntity { Name = "Manual VM", Host = "10.0.0.9" });
        repository.Store.Add(new VirtualMachineEntity { Name = "Found VM", Provider = "HyperV", ProviderId = "hv-9", PowerState = "Running" });
        var model = Create(repository);

        await model.SetScopeAsync("Discovered");

        Assert.Equal("Found VM", Assert.Single(model.Vms).Name);
    }

    [Theory]
    [InlineData(null, "vm-guid", new[] { "vm-guid" })]
    [InlineData("", "vm-guid", new[] { "vm-guid" })]
    [InlineData("SERVER1", "vm-guid", new[] { "SERVER1", "vm-guid" })]
    public void Vmconnect_arguments_are_positional_and_omit_a_blank_server(string? server, string vmId, string[] expected) =>
        Assert.Equal(expected, VmConnectExternalSession.BuildArguments(server, vmId));

    [Fact]
    public void Vmconnect_locator_probes_vmconnect_in_system32()
    {
        // Negative result on machines without the Hyper-V management tools — this dev box
        // among them — must be a clean null, never a throw; positive when File.Exists answers yes.
        Assert.Null(VmConnectLocator.TryGetFullPath(_ => false));
        var found = VmConnectLocator.TryGetFullPath(_ => true);
        Assert.NotNull(found);
        Assert.Equal("vmconnect.exe", Path.GetFileName(found));
    }

    [Fact]
    public async Task Missing_vmconnect_fails_with_an_actionable_message_and_never_throws()
    {
        var process = new FakeConsoleProcess();
        var session = new VmConnectExternalSession(
            Guid.NewGuid(), "Win11 Console", "0ff1e000-0000-0000-0000-000000000001",
            LibraryReliabilityTests.Logs(), process.Start, exeLocator: () => null);

        await session.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Failed, session.State);
        Assert.NotNull(session.LastError);
        Assert.Contains("vmconnect.exe", session.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(process.Starts);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Vmconnect_session_launches_the_console_once_with_the_vm_id()
    {
        var process = new FakeConsoleProcess();
        var session = new VmConnectExternalSession(
            Guid.NewGuid(), "Win11 Console", "0ff1e000-0000-0000-0000-000000000002",
            LibraryReliabilityTests.Logs(), process.Start, exeLocator: () => @"C:\fake\vmconnect.exe");

        await session.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, session.State);
        var psi = Assert.Single(process.Starts);
        Assert.Equal("0ff1e000-0000-0000-0000-000000000002", Assert.Single(psi.ArgumentList));
        Assert.True(string.IsNullOrEmpty(psi.Arguments));
        Assert.Equal(@"C:\fake\vmconnect.exe", psi.FileName);
        Assert.True(session.Capabilities.ExternalClient);
        Assert.Null(session.HostControl);
        await session.DisposeAsync();
    }

    /// <summary>Records starts and drives Exited/Kill without ever spawning a real process.</summary>
    private sealed class FakeConsoleProcess : VmConnectExternalSession.IExternalConsoleProcess
    {
        public List<ProcessStartInfo> Starts { get; } = new();
        public int KillCount { get; private set; }
        public bool Disposed { get; private set; }
        public bool HasExited { get; set; }
        public event EventHandler? Exited;

        public VmConnectExternalSession.IExternalConsoleProcess Start(ProcessStartInfo psi)
        {
            Starts.Add(psi);
            return this;
        }

        public void RaiseExited() => Exited?.Invoke(this, EventArgs.Empty);
        public void Kill() => KillCount++;
        public void Dispose() => Disposed = true;
    }
}
