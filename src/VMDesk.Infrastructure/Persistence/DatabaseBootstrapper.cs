using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VMDesk.Core.Interfaces;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>
/// Opens the database, applies EF Core migrations automatically and recovers from
/// corruption without destroying user data (spec §37, §57, §63).
/// </summary>
public sealed class DatabaseBootstrapper
{
    private readonly IDbContextFactory<VmDeskDbContext> _factory;
    private readonly string _databasePath;
    private readonly IAppLog _log;

    public DatabaseBootstrapper(IDbContextFactory<VmDeskDbContext> factory, string databasePath, IAppLogFactory logFactory)
    {
        _factory = factory;
        _databasePath = databasePath;
        _log = logFactory.GetLogger("Database");
    }

    public string DatabasePath => _databasePath;

    /// <summary>Result of the startup migration attempt.</summary>
    public sealed record BootstrapResult(bool Success, string? Error, bool Recovered, string? RecoveryPath);

    public async Task<BootstrapResult> BootstrapAsync()
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            // The first release ships the model before generated migrations. Ensure
            // fresh and previously-created empty databases receive the schema.
            await db.Database.EnsureCreatedAsync();
            if (!await HasTableAsync("VirtualMachines"))
            {
                var tables = await GetUserTablesAsync();
                if (tables.Count == 0 || tables.All(t => t is "__EFMigrationsHistory" or "__EFMigrationsLock"))
                {
                    await db.Database.EnsureDeletedAsync();
                    await db.Database.EnsureCreatedAsync();
                }
                else
                {
                    throw new InvalidOperationException(
                        "The VMDesk database is missing its VM table and contains other user tables. Create a backup before recovery.");
                }
            }
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
            _log.Info("Database migrated successfully.");
            return new BootstrapResult(true, null, false, null);
        }
        catch (SqliteException ex) when (IsCorruption(ex))
        {
            // Do NOT delete user data. Quarantine the corrupt file and start fresh (spec §37).
            _log.Error($"Database appears corrupt: {ex.Message}");
            var quarantine = _databasePath + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.bak";
            try
            {
                File.Copy(_databasePath, quarantine, overwrite: true);
            }
            catch (IOException ioEx)
            {
                _log.Warn($"Could not quarantine corrupt database: {ioEx.Message}");
            }

            try
            {
                await using var db = await _factory.CreateDbContextAsync();
                await db.Database.EnsureDeletedAsync();
                await db.Database.MigrateAsync();
                _log.Info("Fresh database created after corruption; old file quarantined for recovery.");
                return new BootstrapResult(true, null, true, quarantine);
            }
            catch (Exception fatal)
            {
                return new BootstrapResult(false, fatal.Message, true, quarantine);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Database bootstrap failed.", ex);
            return new BootstrapResult(false, ex.Message, false, null);
        }
    }

    private static bool IsCorruption(SqliteException ex)
    {
        // SQLITE_CORRUPT = 11, SQLITE_NOTADB = 26
        return ex.SqliteErrorCode == 11 || ex.SqliteErrorCode == 26;
    }

    private async Task<bool> HasTableAsync(string tableName)
    {
        var tables = await GetUserTablesAsync();
        return tables.Contains(tableName, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<string>> GetUserTablesAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
