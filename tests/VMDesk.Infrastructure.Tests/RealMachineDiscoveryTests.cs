using System.Diagnostics;
using VMDesk.Application.Services;
using VMDesk.Core.Interfaces;
using VMDesk.Infrastructure.Discovery;
using Xunit;

namespace VMDesk.Infrastructure.Tests;

/// <summary>
/// Machine reality check for this dev box (Hyper-V WMI absent, VirtualBox and VMware not
/// installed): the three real providers plus DiscoveryService must complete without throwing
/// and report Available=false with a useful note. The app must still start because of it.
/// </summary>
public sealed class RealMachineDiscoveryTests
{
    [Fact]
    public async Task Discovery_completes_against_real_providers_on_this_machine()
    {
        var runner = new ProcessRunner();
        var providers = new IVmDiscoveryProvider[]
        {
            new HyperVDiscoveryProvider(runner, TestSupport.Logs()),
            new VirtualBoxDiscoveryProvider(runner, TestSupport.Logs()),
            new VMwareDiscoveryProvider(runner, TestSupport.Logs()),
        };
        var repository = new InMemoryVmRepository();
        var service = new DiscoveryService(
            providers, new DiscoverySyncService(repository, TestSupport.Logs()), TestSupport.Logs());

        var started = Stopwatch.StartNew();
        var report = await service.RunAsync();
        started.Stop();

        // Three statuses, one per provider, in declaration order.
        Assert.Equal(
            new[] { "HyperV", "VirtualBox", "VMware" },
            report.Providers.Select(p => p.Name).ToArray());

        // Every provider either found something or explained why it could not — and the run is capped.
        foreach (var status in report.Providers)
        {
            if (!status.Available)
            {
                Assert.False(string.IsNullOrWhiteSpace(status.Note), $"{status.Name} unavailable without a note");
            }
        }

        Assert.NotNull(report.Sync);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(50), $"run took {started.Elapsed}");
    }
}
