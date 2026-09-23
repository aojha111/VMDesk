using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Infrastructure.Discovery;
using Xunit;

namespace VMDesk.Infrastructure.Tests;

/// <summary>
/// DiscoveryService aggregation: parallel provider runs, per-provider statuses with notes,
/// failures captured never thrown, timeouts noted, and one sync over the union of results.
/// </summary>
public sealed class DiscoveryServiceTests
{
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

    private static (DiscoveryService Service, InMemoryVmRepository Repository) Create(
        IReadOnlyList<IVmDiscoveryProvider> providers, TimeSpan? timeout = null)
    {
        var repository = new InMemoryVmRepository();
        var sync = new DiscoverySyncService(repository, TestSupport.Logs());
        return (new DiscoveryService(providers, sync, TestSupport.Logs(), timeout), repository);
    }

    [Fact]
    public async Task Aggregates_available_and_unavailable_providers_into_statuses()
    {
        var good = new FakeProvider("Good");
        good.OnDiscover = _ => Task.FromResult<IReadOnlyList<DiscoveredVm>>(new[]
        {
            new DiscoveredVm("Good", "g-1", "Alpha", "10.0.0.1", "Running"),
            new DiscoveredVm("Good", "g-2", "Beta", null, "Off"),
        });
        var missing = new FakeProvider("Missing") { Available = false };
        var (service, repository) = Create(new IVmDiscoveryProvider[] { good, missing });

        var report = await service.RunAsync();

        var goodStatus = Assert.Single(report.Providers, s => s.Name == "Good");
        Assert.True(goodStatus.Available);
        Assert.Equal(2, goodStatus.Found);
        Assert.Null(goodStatus.Note);

        var missingStatus = Assert.Single(report.Providers, s => s.Name == "Missing");
        Assert.False(missingStatus.Available);
        Assert.Equal(0, missingStatus.Found);
        Assert.Equal("Missing is not installed.", missingStatus.Note);

        // The union of discoveries went through the Task 5 sync.
        Assert.Equal(new SyncReport(2, 0, 0), report.Sync);
        Assert.Equal(2, repository.Store.Count);
    }

    [Fact]
    public async Task Provider_that_throws_is_captured_as_a_note_and_never_propagates()
    {
        var boom = new FakeProvider("Boom");
        boom.OnDiscover = _ => throw new InvalidOperationException("kaboom");
        var ok = new FakeProvider("Ok");
        ok.OnDiscover = _ => Task.FromResult<IReadOnlyList<DiscoveredVm>>(
            new[] { new DiscoveredVm("Ok", "o-1", "Fine", null, "Running") });
        var (service, _) = Create(new IVmDiscoveryProvider[] { boom, ok });

        var report = await service.RunAsync();

        var boomStatus = Assert.Single(report.Providers, s => s.Name == "Boom");
        Assert.False(boomStatus.Available);
        Assert.Contains("kaboom", boomStatus.Note);
        Assert.Single(report.Providers, s => s.Name == "Ok" && s.Available && s.Found == 1);
        Assert.Equal(new SyncReport(1, 0, 0), report.Sync);
    }

    [Fact]
    public async Task Provider_that_ignores_ct_is_stopped_by_the_timeout_cap()
    {
        var slow = new FakeProvider("Slow");
        // Ignores the linked cancellation token entirely — the cap must still resolve it.
        slow.OnDiscover = async _ =>
        {
            await Task.Delay(Timeout.Infinite, CancellationToken.None).ContinueWith(
                _ => { }); // never completes on its own; the service must not await it forever
            return (IReadOnlyList<DiscoveredVm>)Array.Empty<DiscoveredVm>();
        };
        var (service, _) = Create(new IVmDiscoveryProvider[] { slow }, timeout: TimeSpan.FromMilliseconds(150));

        var report = await service.RunAsync();

        var status = Assert.Single(report.Providers);
        Assert.False(status.Available);
        Assert.Contains("timed out", status.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discovery_that_becomes_unavailable_mid_run_is_reported_unavailable()
    {
        // Matches this dev machine: Hyper-V passes IsAvailable (PowerShell exists) but the WMI
        // namespace is absent, so DiscoverAsync returns empty with a reason.
        var hyperV = new FakeProvider("HyperV");
        hyperV.OnDiscover = async _ =>
        {
            await Task.Yield();
            hyperV.UnavailableReason = "Hyper-V WMI namespace is not available.";
            return (IReadOnlyList<DiscoveredVm>)Array.Empty<DiscoveredVm>();
        };
        var (service, _) = Create(new IVmDiscoveryProvider[] { hyperV });

        var report = await service.RunAsync();

        var status = Assert.Single(report.Providers);
        Assert.False(status.Available);
        Assert.Equal("Hyper-V WMI namespace is not available.", status.Note);
    }

    [Fact]
    public async Task Runs_providers_in_parallel_not_serially()
    {
        IVmDiscoveryProvider MakeDelayed(int index)
        {
            var p = new FakeProvider($"P{index}");
            p.OnDiscover = async _ =>
            {
                await Task.Delay(300);
                return (IReadOnlyList<DiscoveredVm>)Array.Empty<DiscoveredVm>();
            };
            return p;
        }

        var providers = new[] { MakeDelayed(0), MakeDelayed(1), MakeDelayed(2) };
        var (service, _) = Create(providers);

        var start = Environment.TickCount64;
        var report = await service.RunAsync();
        var elapsed = Environment.TickCount64 - start;

        Assert.Equal(3, report.Providers.Count);
        Assert.True(elapsed < 800, $"expected parallel execution, took {elapsed}ms");
    }

    [Fact]
    public async Task Providers_that_did_not_scan_cleanly_never_mark_their_rows_unknown()
    {
        // The real state of this machine: no Hyper-V namespace, no VBoxManage, no vmrun. A scan
        // that proved nothing must leave the catalog's power states alone.
        var (service, repository) = Create(new IVmDiscoveryProvider[] { new FakeProvider("HyperV") { Available = false } });
        repository.Store.Add(Discovered("HyperV", "hv-1", "Already Known", "Running"));

        var report = await service.RunAsync();

        Assert.Equal(0, report.Sync.StaleMarked);
        Assert.Equal("Running", repository.Store.Single().PowerState);
    }

    [Fact]
    public async Task A_clean_scan_marks_its_own_disappeared_vm_unknown_but_leaves_other_providers()
    {
        // Partial failure: Hyper-V scanned and lost a VM, VirtualBox is simply not installed here.
        var hyperV = new FakeProvider("HyperV");
        hyperV.OnDiscover = _ => Task.FromResult<IReadOnlyList<DiscoveredVm>>(
            new[] { new DiscoveredVm("HyperV", "hv-alive", "Alive", null, "Running") });
        var missingBox = new FakeProvider("VirtualBox") { Available = false };

        var (service, repository) = Create(new IVmDiscoveryProvider[] { hyperV, missingBox });
        repository.Store.Add(Discovered("HyperV", "hv-gone", "Deleted In Hyper-V", "Running"));
        repository.Store.Add(Discovered("VirtualBox", "vb-1", "Box", "Running"));

        var report = await service.RunAsync();

        Assert.Equal(1, report.Sync.StaleMarked);
        Assert.Equal("Unknown", repository.Store.Single(v => v.ProviderId == "hv-gone").PowerState);
        Assert.Equal("Running", repository.Store.Single(v => v.ProviderId == "vb-1").PowerState);
        Assert.Equal(1, report.Sync.AvailableProviderCount);
    }

    private static VirtualMachineEntity Discovered(string provider, string providerId, string name, string powerState) =>
        new()
        {
            Name = name,
            Provider = provider,
            ProviderId = providerId,
            PowerState = powerState,
        };

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_hanging()
    {
        var waiting = new FakeProvider("Waiting");
        waiting.OnDiscover = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return (IReadOnlyList<DiscoveredVm>)Array.Empty<DiscoveredVm>();
        };
        var (service, _) = Create(new IVmDiscoveryProvider[] { waiting });
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(cts.Token));
    }
}
