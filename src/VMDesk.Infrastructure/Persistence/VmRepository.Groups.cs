using Microsoft.EntityFrameworkCore;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>Groups + tags portion of IVmRepository.</summary>
public sealed partial class VmRepository
{
    public async Task<IReadOnlyList<GroupEntity>> GetGroupsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name).AsNoTracking().ToListAsync();
    }

    public async Task<GroupEntity> AddGroupAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Groups.FirstOrDefaultAsync(g => g.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var maxOrder = await db.Groups.AnyAsync() ? await db.Groups.MaxAsync(g => g.SortOrder) : 0;
        var group = new GroupEntity { Name = name, SortOrder = maxOrder + 1 };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    public async Task UpdateGroupAsync(GroupEntity group)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Groups.Update(group);
        await db.SaveChangesAsync();
    }

    public async Task DeleteGroupAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Groups.Where(g => g.Id == id).ExecuteDeleteAsync();
    }

    public async Task<IReadOnlyList<TagEntity>> GetTagsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tags.OrderBy(t => t.Name).AsNoTracking().ToListAsync();
    }

    public async Task SetTagsAsync(Guid vmId, IReadOnlyList<string> tags)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var normalized = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in normalized)
        {
            if (!await db.Tags.AnyAsync(t => t.Name == name))
            {
                db.Tags.Add(new TagEntity { Name = name });
            }
        }

        await db.SaveChangesAsync();

        var tagIds = await db.Tags
            .Where(t => normalized.Contains(t.Name))
            .Select(t => t.Id)
            .ToListAsync();

        var existingLinks = db.VmTags.Where(l => l.VmId == vmId);
        db.VmTags.RemoveRange(existingLinks);
        foreach (var tagId in tagIds)
        {
            db.VmTags.Add(new VmTagEntity { VmId = vmId, TagId = tagId });
        }

        await db.SaveChangesAsync();
    }
}
