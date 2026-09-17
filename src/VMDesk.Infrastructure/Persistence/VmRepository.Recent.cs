using Microsoft.EntityFrameworkCore;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>Favorites, recent connections and connection recording portion of IVmRepository.</summary>
public sealed partial class VmRepository
{
    public async Task SetFavoriteAsync(Guid id, bool favorite)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.VirtualMachines.Where(v => v.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(v => v.Favorite, favorite).SetProperty(v => v.UpdatedAt, DateTimeOffset.UtcNow));
    }

    public async Task RecordConnectionAsync(Guid id, bool success, string? error)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var vm = await db.VirtualMachines.FirstOrDefaultAsync(v => v.Id == id);
        if (vm is null)
        {
            return;
        }

        vm.LastConnectedAt = DateTimeOffset.UtcNow;
        vm.LastConnectionStatus = success ? ConnectionState.Connected.ToString() : ConnectionState.Failed.ToString();
        vm.LastConnectionError = error ?? string.Empty;
        vm.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        db.RecentConnections.Add(new RecentConnectionEntity
        {
            VmId = vm.Id,
            VmName = vm.Name,
            Host = vm.Host,
            ConnectedAt = DateTimeOffset.UtcNow,
            Success = success
        });

        var stale = await db.RecentConnections
            .OrderByDescending(r => r.ConnectedAt)
            .Skip(100)
            .Select(r => r.Id)
            .ToListAsync();
        if (stale.Count > 0)
        {
            await db.RecentConnections.Where(r => stale.Contains(r.Id)).ExecuteDeleteAsync();
        }

        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<RecentConnectionEntity>> GetRecentConnectionsAsync(int count)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.RecentConnections
            .OrderByDescending(r => r.ConnectedAt)
            .Take(count)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task ClearRecentConnectionsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.RecentConnections.ExecuteDeleteAsync();
    }
}
