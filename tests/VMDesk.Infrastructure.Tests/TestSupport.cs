using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Infrastructure.Tests;

/// <summary>
/// Shared fakes: no-op logging, in-memory repository and a scriptable process runner.
/// </summary>
internal static class TestSupport
{
    public static IAppLogFactory Logs() => new NoopLogFactory();

    private sealed class NoopLogFactory : IAppLogFactory
    {
        public IAppLog GetLogger(string category) => new NoopLog();
        public void Flush() { }
    }

    private sealed class NoopLog : IAppLog
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}

/// <summary>In-memory IVmRepository; only the members DiscoverySyncService touches are functional.</summary>
internal sealed class InMemoryVmRepository : IVmRepository
{
    public List<VirtualMachineEntity> Store { get; } = new();

    public Task<IReadOnlyList<VirtualMachineEntity>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(Store.ToList());

    public Task<VirtualMachineEntity?> GetAsync(Guid id) =>
        Task.FromResult(Store.FirstOrDefault(v => v.Id == id));

    public Task<VirtualMachineEntity> AddAsync(VirtualMachineEntity vm)
    {
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
