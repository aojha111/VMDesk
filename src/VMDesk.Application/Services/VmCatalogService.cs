using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;

namespace VMDesk.Application.Services;

/// <summary>
/// VM library facade: CRUD, duplicate, tags, favorites (spec §2, §20-22).
/// All operations are async and never block the UI thread.
/// </summary>
public sealed class VmCatalogService
{
    private readonly IVmRepository _repository;
    private readonly ICredentialStore _credentialStore;
    private readonly IAppLog _log;

    public VmCatalogService(IVmRepository repository, ICredentialStore credentialStore, IAppLogFactory logFactory)
    {
        _repository = repository;
        _credentialStore = credentialStore;
        _log = logFactory.GetLogger("VmCatalog");
    }

    public event EventHandler<VirtualMachineEntity>? VmChanged;

    public async Task<IReadOnlyList<VirtualMachineEntity>> GetAllAsync() => await _repository.GetAllAsync();

    public async Task<VirtualMachineEntity?> GetAsync(Guid id) => await _repository.GetAsync(id);

    public async Task<VirtualMachineEntity> AddAsync(VirtualMachineEntity vm)
    {
        vm.Id = Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(vm.CredentialReference))
        {
            vm.CredentialReference = VmFilter.CredentialReferenceFor(vm.Id);
        }
        vm.CreatedAt = DateTimeOffset.UtcNow;
        vm.UpdatedAt = DateTimeOffset.UtcNow;
        var saved = await _repository.AddAsync(vm);
        VmChanged?.Invoke(this, saved);
        _log.Info($"Added VM '{saved.Name}'.");
        return saved;
    }

    public async Task UpdateAsync(VirtualMachineEntity vm)
    {
        vm.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateAsync(vm);
        VmChanged?.Invoke(this, vm);
        _log.Info($"Updated VM '{vm.Name}'.");
    }

    public async Task DeleteAsync(VirtualMachineEntity vm)
    {
        // Credentials are managed independently and may be shared by multiple VMs.
        // Removing library metadata must never remove a saved login.
        await _repository.DeleteAsync(vm.Id);
        VmChanged?.Invoke(this, vm);
        _log.Info($"Deleted VM '{vm.Name}'.");
    }

    public async Task<VirtualMachineEntity> DuplicateAsync(VirtualMachineEntity source)
    {
        var copy = VmFilter.CloneForDuplicate(source);
        return await AddAsync(copy);
    }

    public async Task SetFavoriteAsync(VirtualMachineEntity vm, bool favorite)
    {
        vm.Favorite = favorite;
        await _repository.SetFavoriteAsync(vm.Id, favorite);
        VmChanged?.Invoke(this, vm);
    }

    public async Task SetTagsAsync(VirtualMachineEntity vm, IReadOnlyList<string> tags)
    {
        await _repository.SetTagsAsync(vm.Id, tags);
        VmChanged?.Invoke(this, vm);
    }

    public async Task<IReadOnlyList<GroupEntity>> GetGroupsAsync() => await _repository.GetGroupsAsync();

    public async Task<GroupEntity> AddGroupAsync(string name) => await _repository.AddGroupAsync(name);

    public async Task DeleteGroupAsync(GroupEntity group) => await _repository.DeleteGroupAsync(group.Id);

    public async Task<IReadOnlyList<TagEntity>> GetTagsAsync() => await _repository.GetTagsAsync();

    public async Task<IReadOnlyList<RecentConnectionEntity>> GetRecentAsync(int count) =>
        await _repository.GetRecentConnectionsAsync(count);

    public Task ClearRecentAsync() => _repository.ClearRecentConnectionsAsync();
}
