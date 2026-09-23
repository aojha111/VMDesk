using Microsoft.Data.Sqlite;
using VMDesk.Core.Interfaces;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>
/// Idempotent, migration-free column top-ups for SQLite. This repository's user database was built
/// with <c>EnsureCreatedAsync</c>, which never adds columns to an existing schema, so new columns on
/// an already-shipped table are added here after migration instead of via an EF migration.
/// Tasks 8 and 13 reuse this helper for their own columns.
/// Identifiers are compile-time constants because PRAGMA/ALTER cannot take bound parameters.
/// </summary>
public static class SqliteColumnMigrations
{
    /// <summary>
    /// Adds <c>column</c> to <c>table</c> only when <c>PRAGMA table_info</c> does not list it, so it is safe
    /// to run on a brand-new database and on one that already carries the column. Returns true when altered.
    /// </summary>
    public static async Task<bool> EnsureColumnAsync(
        string databasePath,
        IAppLog log,
        string table,
        string column,
        string type,
        string safeDefault)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Cache=Shared");
        await connection.OpenAsync();

        var columnExists = false;
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await check.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk.
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    columnExists = true;
                    break;
                }
            }
        }

        if (columnExists)
        {
            return false;
        }

        await using (var alter = connection.CreateCommand())
        {
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type} NOT NULL DEFAULT {safeDefault};";
            await alter.ExecuteNonQueryAsync();
        }

        log.Info($"Added missing column {table}.{column} ({type} NOT NULL DEFAULT {safeDefault}).");
        return true;
    }
}
