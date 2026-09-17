using VMDesk.Core.Interfaces;

namespace VMDesk.Application.Services;

/// <summary>Timestamped SQLite backup/restore with validation (spec §26).</summary>
public sealed class BackupRestoreService : IBackupService
{
    private readonly Func<string> _databasePath;
    private readonly IAppLog _log;

    public BackupRestoreService(Func<string> databasePath, IAppLogFactory logFactory)
    {
        _databasePath = databasePath;
        _log = logFactory.GetLogger("Backup");
    }

    public async Task<string> BackupAsync(string? destinationDirectory = null)
    {
        var source = _databasePath();
        var dir = destinationDirectory ?? Path.Combine(Path.GetDirectoryName(source)!, "Backups");
        Directory.CreateDirectory(dir);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(dir, $"vmdesk-{stamp}.db");

        await Task.Run(() =>
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
        });

        var ok = await ValidateAsync(target);
        if (!ok)
        {
            File.Delete(target);
            throw new InvalidOperationException("Backup validation failed; backup removed.");
        }

        _log.Info($"Database backed up to '{target}'.");
        return target;
    }

    public async Task RestoreAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup file not found.", backupPath);
        }

        var valid = await ValidateAsync(backupPath);
        if (!valid)
        {
            throw new InvalidOperationException("The selected file is not a valid VMDesk database.");
        }

        var target = _databasePath();

        // Automatic backup of the current database before restore (spec §26).
        if (File.Exists(target))
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var preRestore = Path.Combine(Path.GetDirectoryName(target)!, "Backups", $"pre-restore-{stamp}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(preRestore)!);
            File.Copy(target, preRestore, overwrite: true);
            _log.Info($"Pre-restore backup created: '{preRestore}'.");
        }

        // Copy over WAL/SHM companions too so the restored file is complete.
        foreach (var ext in new[] { "-wal", "-shm" })
        {
            var srcWal = backupPath + ext;
            var dstWal = target + ext;
            if (File.Exists(srcWal))
            {
                File.Copy(srcWal, dstWal, overwrite: true);
            }
            else if (File.Exists(dstWal))
            {
                File.Delete(dstWal);
            }
        }

        await Task.Run(() => File.Copy(backupPath, target, overwrite: true));
        _log.Info($"Database restored from '{backupPath}'. Restart required.");
    }

    public async Task<bool> ValidateAsync(string databasePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(databasePath))
                {
                    return false;
                }

                var header = new byte[16];
                using (var fs = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 100)
                    {
                        return false;
                    }

                    fs.ReadExactly(header, 0, header.Length);
                }

                var magic = "SQLite format 3\0"u8;
                return header.AsSpan().SequenceEqual(magic);
            }
            catch (IOException)
            {
                return false;
            }
        });
    }
}
