using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;

namespace VMDesk.Application.Services;

/// <summary>
/// A VM reported by a discovery provider; catalog rows are matched on (Provider, ProviderId).
/// </summary>
public sealed record DiscoveredVm(string Provider, string ProviderId, string Name, string? Host, string PowerState);

/// <summary>
/// Outcome of one discovery sync. Counts reflect real changes: re-syncing identical data yields (0, 0, 0).
/// </summary>
public sealed record SyncReport(int Added, int Updated, int StaleMarked);

/// <summary>
/// Reconciles the VM catalog with discovery scan results. Manual rows (user-created) are never
/// matched, modified, or deleted. A discovered VM that disappears from the scan keeps its row —
/// the user may have set credentials or notes on it — but its PowerState becomes "Unknown".
/// No code path here deletes rows. Credentials are never part of discovery: CredentialReference is
/// only ever read from or preserved by the user, never written by sync.
/// </summary>
public sealed class DiscoverySyncService
{
    /// <summary>Provider value for user-created rows; sync treats those rows as read-only.</summary>
    public const string ManualProvider = "Manual";

    /// <summary>Power state stored for discovered rows a scan no longer reports.</summary>
    public const string UnknownPowerState = "Unknown";

    private readonly IVmRepository _repository;
    private readonly IAppLog _log;

    public DiscoverySyncService(IVmRepository repository, IAppLogFactory logFactory)
    {
        _repository = repository;
        _log = logFactory.GetLogger("DiscoverySync");
    }

    public async Task<SyncReport> SyncAsync(IReadOnlyList<DiscoveredVm> found)
    {
        var all = await _repository.GetAllAsync();
        // Manual rows are excluded from matching and mutation entirely.
        var discovered = all
            .Where(v => !string.Equals(v.Provider, ManualProvider, StringComparison.Ordinal))
            .ToList();
        var matched = new HashSet<Guid>();
        var added = 0;
        var updated = 0;
        var staleMarked = 0;

        foreach (var item in found)
        {
            // First unmatched row wins; without a unique index duplicates are possible and the
            // extras get stale-marked until they are cleaned up.
            var existing = discovered.FirstOrDefault(v => !matched.Contains(v.Id)
                && string.Equals(v.Provider, item.Provider, StringComparison.Ordinal)
                && string.Equals(v.ProviderId, item.ProviderId, StringComparison.Ordinal));

            if (existing is not null)
            {
                matched.Add(existing.Id);
                var changed = false;

                if (existing.Name != item.Name)
                {
                    existing.Name = item.Name;
                    changed = true;
                }

                // A null scan host means "not reported", not "clear it"; never wipe user-set data.
                if (item.Host is not null && existing.Host != item.Host)
                {
                    existing.Host = item.Host;
                    changed = true;
                }

                if (existing.PowerState != item.PowerState)
                {
                    existing.PowerState = item.PowerState;
                    changed = true;
                }

                if (changed)
                {
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    await _repository.UpdateAsync(existing);
                    updated++;
                }

                continue;
            }

            var vm = new VirtualMachineEntity
            {
                Id = Guid.NewGuid(),
                Name = item.Name,
                Host = item.Host ?? string.Empty,
                Provider = item.Provider,
                ProviderId = item.ProviderId,
                PowerState = item.PowerState,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            // Credential reference follows the catalog convention; the user fills it in via the editor.
            vm.CredentialReference = VmFilter.CredentialReferenceFor(vm.Id);
            await _repository.AddAsync(vm);
            added++;
        }

        foreach (var vm in discovered)
        {
            if (matched.Contains(vm.Id) || vm.PowerState == UnknownPowerState)
            {
                continue;
            }

            vm.PowerState = UnknownPowerState;
            vm.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(vm);
            staleMarked++;
        }

        _log.Info($"Discovery sync finished: {added} added, {updated} updated, {staleMarked} marked stale.");
        return new SyncReport(added, updated, staleMarked);
    }
}
