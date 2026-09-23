using VMDesk.Core.Interfaces;

namespace VMDesk.Application.Services;

/// <summary>Outcome of scanning one provider during a discovery run.</summary>
public sealed record ProviderStatus(string Name, bool Available, int Found, string? Note);

/// <summary>Result of one full discovery run: per-provider statuses plus the catalog sync outcome.</summary>
public sealed record DiscoveryRunReport(IReadOnlyList<ProviderStatus> Providers, ScanReport Sync);

/// <summary>
/// Runs every registered discovery provider in parallel, aggregates the findings into one
/// catalog sync (Task 5 DiscoverySyncService) and reports per-provider outcomes. No provider
/// failure ever propagates: each one is captured in a <see cref="ProviderStatus.Note"/> and
/// capped at 15 s. Caller cancellation still propagates.
/// </summary>
public sealed class DiscoveryService
{
    /// <summary>Per-provider wall-clock cap from the spec; a scan past it is noted as timed out.</summary>
    public static readonly TimeSpan DefaultProviderTimeout = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyList<IVmDiscoveryProvider> _providers;
    private readonly DiscoverySyncService _sync;
    private readonly IAppLog _log;
    private readonly TimeSpan _providerTimeout;

    public DiscoveryService(
        IReadOnlyList<IVmDiscoveryProvider> providers,
        DiscoverySyncService sync,
        IAppLogFactory logFactory,
        TimeSpan? providerTimeout = null)
    {
        _providers = providers;
        _sync = sync;
        _log = logFactory.GetLogger("Discovery");
        _providerTimeout = providerTimeout ?? DefaultProviderTimeout;
    }

    public async Task<DiscoveryRunReport> RunAsync(CancellationToken ct = default)
    {
        // Parallel: the 15 s caps are per provider, so the run costs as much as the slowest scan.
        var results = await Task.WhenAll(_providers.Select(p => ScanAsync(p, ct))).ConfigureAwait(false);

        var statuses = results.Select(r => r.Status).ToList();
        var found = results.SelectMany(r => r.Vms).ToList();
        // Only providers that actually scanned may have their rows stale-marked; a missing
        // hypervisor reporting nothing is silence, not evidence its VMs disappeared.
        var clean = statuses.Where(s => s.Available).Select(s => s.Name).ToList();
        var sync = await _sync.SyncAsync(found, clean).ConfigureAwait(false);

        return new DiscoveryRunReport(statuses, sync);
    }

    private async Task<(ProviderStatus Status, IReadOnlyList<DiscoveredVm> Vms)> ScanAsync(
        IVmDiscoveryProvider provider, CancellationToken ct)
    {
        if (!provider.IsAvailable())
        {
            return Report(new ProviderStatus(provider.ProviderName, false, 0,
                provider.UnavailableReason ?? $"{provider.ProviderName} is not available."));
        }

        IReadOnlyList<DiscoveredVm> vms;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_providerTimeout);

        Task<IReadOnlyList<DiscoveredVm>> running;
        try
        {
            running = provider.DiscoverAsync(timeoutCts.Token);
        }
        catch (Exception ex)
        {
            return Report(Failure(provider, ex));
        }

        // Belt and braces: a provider that ignores its token must not hang the run past the cap.
        var winner = await Task.WhenAny(running, Task.Delay(_providerTimeout, CancellationToken.None))
            .ConfigureAwait(false);
        if (winner != running && !running.IsCompleted)
        {
            _ = running.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException("Discovery was cancelled.", ct);
            }

            return Report(new ProviderStatus(provider.ProviderName, false, 0,
                $"timed out after {_providerTimeout.TotalSeconds:0} seconds"));
        }

        try
        {
            vms = await running.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Report(new ProviderStatus(provider.ProviderName, false, 0,
                $"timed out after {_providerTimeout.TotalSeconds:0} seconds"));
        }
        catch (Exception ex)
        {
            // Providers promise not to throw; a broken one still only costs a note.
            return Report(Failure(provider, ex));
        }

        // "Empty because it failed" (e.g. Hyper-V namespace absent on this machine) is unavailable,
        // "empty because nothing exists" (healthy scan, no VMs) keeps Available=true.
        if (vms.Count == 0 && provider.UnavailableReason is { } reason)
        {
            return Report(new ProviderStatus(provider.ProviderName, false, 0, reason));
        }

        return Report(new ProviderStatus(provider.ProviderName, true, vms.Count, null), vms);
    }

    private (ProviderStatus Status, IReadOnlyList<DiscoveredVm> Vms) Report(
        ProviderStatus status, IReadOnlyList<DiscoveredVm>? vms = null)
    {
        _log.Info(status.Note is null
            ? $"Discovery provider {status.Name}: found {status.Found} VM(s)."
            : $"Discovery provider {status.Name}: unavailable, found {status.Found} VM(s) — {status.Note}");
        return (status, vms ?? Array.Empty<DiscoveredVm>());
    }

    private ProviderStatus Failure(IVmDiscoveryProvider provider, Exception ex)
    {
        _log.Error($"Discovery provider {provider.ProviderName} failed.", ex);
        return new ProviderStatus(provider.ProviderName, false, 0, ex.Message);
    }
}
