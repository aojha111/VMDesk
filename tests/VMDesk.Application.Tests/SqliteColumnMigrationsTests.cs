using System.IO;
using Microsoft.Data.Sqlite;
using VMDesk.Core.Interfaces;
using VMDesk.Infrastructure.Persistence;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// Real-file tests for the idempotent column top-up the database bootstrapper runs after
/// Migrate/EnsureCreated (EnsureCreatedAsync never adds columns to an existing schema).
/// </summary>
public sealed class SqliteColumnMigrationsTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"vmdesk-column-migrations-{Guid.NewGuid():N}.db");
    private readonly RecordingLog _log = new();
    private bool _disposed;

    private sealed class RecordingLog : IAppLog
    {
        public List<string> Messages { get; } = new();
        public void Debug(string message) => Messages.Add(message);
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value as string;
    }

    [Fact]
    public async Task EnsureColumn_adds_once_is_idempotent_and_keeps_existing_rows_readable()
    {
        await ExecuteAsync("CREATE TABLE VirtualMachines (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL DEFAULT '');");
        await ExecuteAsync("INSERT INTO VirtualMachines (Id, Name) VALUES ('1', 'legacy');");

        var added = await SqliteColumnMigrations.EnsureColumnAsync(
            _databasePath, _log, "VirtualMachines", "Provider", "TEXT", "'Manual'");

        Assert.True(added);
        Assert.Single(_log.Messages); // what it added is logged

        // Re-running is a no-op: no exception, no duplicate log entry.
        added = await SqliteColumnMigrations.EnsureColumnAsync(
            _databasePath, _log, "VirtualMachines", "Provider", "TEXT", "'Manual'");
        Assert.False(added);
        Assert.Single(_log.Messages);

        // Pre-existing row sees the safe default and the column is NOT NULL.
        Assert.Equal("Manual", await ScalarAsync("SELECT Provider FROM VirtualMachines WHERE Id = '1';"));
        await ExecuteAsync("INSERT INTO VirtualMachines (Id, Name) VALUES ('2', 'fresh');");
        Assert.Equal("Manual", await ScalarAsync("SELECT Provider FROM VirtualMachines WHERE Id = '2';"));
    }

    [Fact]
    public async Task EnsureColumn_adds_every_bootstrapper_column_to_a_pre_task5_schema()
    {
        await ExecuteAsync("CREATE TABLE VirtualMachines (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL DEFAULT '');");

        Assert.True(await SqliteColumnMigrations.EnsureColumnAsync(_databasePath, _log, "VirtualMachines", "Provider", "TEXT", "'Manual'"));
        Assert.True(await SqliteColumnMigrations.EnsureColumnAsync(_databasePath, _log, "VirtualMachines", "ProviderId", "TEXT", "''"));
        Assert.True(await SqliteColumnMigrations.EnsureColumnAsync(_databasePath, _log, "VirtualMachines", "PowerState", "TEXT", "''"));

        await ExecuteAsync("INSERT INTO VirtualMachines (Id, Name) VALUES ('1', 'row');");
        Assert.Equal("Manual", await ScalarAsync("SELECT Provider FROM VirtualMachines WHERE Id = '1';"));
        Assert.Equal(string.Empty, await ScalarAsync("SELECT ProviderId FROM VirtualMachines WHERE Id = '1';"));
        Assert.Equal(string.Empty, await ScalarAsync("SELECT PowerState FROM VirtualMachines WHERE Id = '1';"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SqliteConnection.ClearAllPools(); // release pooled handles so the temp file can go
        try
        {
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file; nothing user-facing depends on it.
        }
    }
}
