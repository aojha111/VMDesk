using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>Settings persisted in the SQLite ApplicationSettings table (spec §28, §49).</summary>
public sealed class SqliteSettingsService : ISettingsService
{
    private readonly IDbContextFactory<VmDeskDbContext> _factory;
    private readonly IAppLog _log;

    public SqliteSettingsService(IDbContextFactory<VmDeskDbContext> factory, IAppLogFactory logFactory)
    {
        _factory = factory;
        _log = logFactory.GetLogger("Settings");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public async Task<AppSettingsModel> GetAsync()
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var row = await db.ApplicationSettings.FirstOrDefaultAsync(s => s.Key == "app.settings");
            if (row is null || string.IsNullOrWhiteSpace(row.Value))
            {
                return new AppSettingsModel();
            }

            return JsonSerializer.Deserialize<AppSettingsModel>(row.Value, JsonOptions) ?? new AppSettingsModel();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load settings; using defaults.", ex);
            return new AppSettingsModel();
        }
    }

    public async Task SaveAsync(AppSettingsModel settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.ApplicationSettings.FirstOrDefaultAsync(s => s.Key == "app.settings");
        if (row is null)
        {
            db.ApplicationSettings.Add(new Core.Entities.ApplicationSettingEntity { Key = "app.settings", Value = json });
        }
        else
        {
            row.Value = json;
        }

        await db.SaveChangesAsync();
    }

    public async Task<T> GetValueAsync<T>(string key, T defaultValue)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var row = await db.ApplicationSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (row is null || string.IsNullOrWhiteSpace(row.Value))
            {
                return defaultValue;
            }

            return JsonSerializer.Deserialize<T>(row.Value, JsonOptions) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public async Task SetValueAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.ApplicationSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            db.ApplicationSettings.Add(new Core.Entities.ApplicationSettingEntity { Key = key, Value = json });
        }
        else
        {
            row.Value = json;
        }

        await db.SaveChangesAsync();
    }
}
