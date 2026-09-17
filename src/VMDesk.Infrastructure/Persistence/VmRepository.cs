using Microsoft.EntityFrameworkCore;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>EF Core implementation of IVmRepository (spec §27). Retries on SQLite busy locks.</summary>
public sealed partial class VmRepository : IVmRepository
{
    private readonly IDbContextFactory<VmDeskDbContext> _factory;
    private readonly IAppLog _log;

    public VmRepository(IDbContextFactory<VmDeskDbContext> factory, IAppLogFactory logFactory)
    {
        _factory = factory;
        _log = logFactory.GetLogger("Repository");
    }

    public async Task<IReadOnlyList<VirtualMachineEntity>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.VirtualMachines
            .Include(v => v.Group)
            .Include(v => v.Tags).ThenInclude(t => t.Tag)
            .AsNoTracking()
            .OrderByDescending(v => v.Favorite)
            .ThenBy(v => v.Name)
            .ToListAsync();
    }

    public async Task<VirtualMachineEntity?> GetAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.VirtualMachines
            .Include(v => v.Group)
            .Include(v => v.Tags).ThenInclude(t => t.Tag)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<VirtualMachineEntity> AddAsync(VirtualMachineEntity vm)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var groupId = vm.GroupId;
        var tags = vm.Tags.Select(t => t.TagId).ToList();
        vm.GroupId = null;
        vm.Tags = new List<VmTagEntity>();
        db.VirtualMachines.Add(vm);
        await db.SaveChangesAsync();

        vm.GroupId = groupId;
        vm.Tags = new List<VmTagEntity>();
        await using var db2 = await _factory.CreateDbContextAsync();
        var entity = await db2.VirtualMachines.FirstAsync(v => v.Id == vm.Id);
        entity.GroupId = groupId;
        foreach (var tagId in tags)
        {
            db2.VmTags.Add(new VmTagEntity { VmId = vm.Id, TagId = tagId });
        }

        await db2.SaveChangesAsync();
        _log.Info($"VM '{vm.Name}' persisted.");
        return vm;
    }

    public async Task UpdateAsync(VirtualMachineEntity vm)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var entity = await db.VirtualMachines
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == vm.Id)
            ?? throw new KeyNotFoundException($"VM {vm.Id} not found.");

        VmMapper.CopyFields(vm, entity);

        var wanted = vm.Tags.Select(t => t.TagId).ToHashSet();
        var current = entity.Tags.Select(t => t.TagId).ToHashSet();

        foreach (var remove in current.Except(wanted))
        {
            var link = entity.Tags.First(t => t.TagId == remove);
            db.VmTags.Remove(link);
        }

        foreach (var add in wanted.Except(current))
        {
            db.VmTags.Add(new VmTagEntity { VmId = vm.Id, TagId = add });
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.VirtualMachines.Where(v => v.Id == id).ExecuteDeleteAsync();
        await db.RecentConnections.Where(r => r.VmId == id).ExecuteDeleteAsync();
        _log.Info($"VM {id} removed.");
    }
}
