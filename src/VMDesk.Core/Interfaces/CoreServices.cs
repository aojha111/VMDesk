using VMDesk.Core.Entities;
using VMDesk.Core.Models;

namespace VMDesk.Core.Interfaces;

/// <summary>
/// Application file logging abstraction. Structured, redacted (spec §30):
/// passwords, clipboard contents and credential values are never passed in.
/// </summary>
public interface IAppLog
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public interface IAppLogFactory
{
    IAppLog GetLogger(string category);
    void Flush();
}

/// <summary>Secure credential storage backed by Windows Credential Manager (spec §7).</summary>
public interface ICredentialStore
{
    Task SaveCredentialAsync(string reference, string username, string password);
    Task<CredentialData?> GetCredentialAsync(string reference);
    Task DeleteCredentialAsync(string reference);
    Task<bool> CredentialExistsAsync(string reference);
    Task<IReadOnlyList<SavedCredential>> GetCredentialsAsync();
    Task ClearAllCredentialsAsync();
    Task<bool> IsAvailableAsync();
}

/// <summary>VM persistence (spec §27).</summary>
public interface IVmRepository
{
    Task<IReadOnlyList<VirtualMachineEntity>> GetAllAsync();
    Task<VirtualMachineEntity?> GetAsync(Guid id);
    Task<VirtualMachineEntity> AddAsync(VirtualMachineEntity vm);
    Task UpdateAsync(VirtualMachineEntity vm);
    Task DeleteAsync(Guid id);
    Task<IReadOnlyList<GroupEntity>> GetGroupsAsync();
    Task<GroupEntity> AddGroupAsync(string name);
    Task UpdateGroupAsync(GroupEntity group);
    Task DeleteGroupAsync(Guid id);
    Task<IReadOnlyList<TagEntity>> GetTagsAsync();
    Task SetTagsAsync(Guid vmId, IReadOnlyList<string> tags);
    Task SetFavoriteAsync(Guid id, bool favorite);
    Task RecordConnectionAsync(Guid id, bool success, string? error);
    Task<IReadOnlyList<RecentConnectionEntity>> GetRecentConnectionsAsync(int count);
    Task ClearRecentConnectionsAsync();
}

/// <summary>Persistent application settings stored in SQLite (spec §28, §49, §83).</summary>
public interface ISettingsService
{
    Task<AppSettingsModel> GetAsync();
    Task SaveAsync(AppSettingsModel settings);
    Task<T> GetValueAsync<T>(string key, T defaultValue);
    Task SetValueAsync<T>(string key, T value);
}
