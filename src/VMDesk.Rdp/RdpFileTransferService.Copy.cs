using System.Diagnostics;
using System.IO;
using VMDesk.Core.Enums;
using VMDesk.Core.Models;

namespace VMDesk.Rdp;

/// <summary>Chunked copy engine with progress + cancellation for file transfers.</summary>
public sealed partial class RdpFileTransferService
{
    private static async Task CopyWithProgressAsync(
        IReadOnlyList<string> sources,
        string targetDirectory,
        TransferDirection direction,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        long totalBytes = 0;
        foreach (var path in sources)
        {
            if (File.Exists(path))
            {
                totalBytes += new FileInfo(path).Length;
            }
            else if (Directory.Exists(path))
            {
                totalBytes += Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            }
        }

        long copiedBytes = 0;
        var timer = Stopwatch.StartNew();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var target = Path.Combine(targetDirectory, name);

            if (Directory.Exists(source))
            {
                await CopyDirectoryAsync(source, target, direction, progress, timer, copiedBytes, totalBytes, cancellationToken);
                continue;
            }

            await CopyFileAsync(source, target, overwrite: true, direction, progress, timer, copiedBytes, totalBytes, cancellationToken);
        }

        progress?.Report(new TransferProgress(direction, string.Empty, totalBytes, totalBytes, totalBytes / Math.Max(0.001, timer.Elapsed.TotalSeconds), 100));
    }

    private static async Task CopyFileAsync(
        string source,
        string target,
        bool overwrite,
        TransferDirection direction,
        IProgress<TransferProgress>? progress,
        Stopwatch timer,
        long copiedBase,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        if (File.Exists(target) && !overwrite)
        {
            // Safety (spec §91): never silently overwrite.
            throw new IOException("Target file already exists: " + target);
        }

        const int bufferSize = 4 * 1024 * 1024;
        var buffer = new byte[bufferSize];
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        int read;
        var fileName = Path.GetFileName(source);
        var copied = copiedBase;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            var elapsed = Math.Max(0.001, timer.Elapsed.TotalSeconds);
            progress?.Report(new TransferProgress(
                direction,
                fileName,
                copied,
                totalBytes,
                copied / elapsed,
                totalBytes > 0 ? (int)(copied * 100 / totalBytes) : 100));
        }
    }

    private static async Task CopyDirectoryAsync(
        string sourceDir,
        string targetDir,
        TransferDirection direction,
        IProgress<TransferProgress>? progress,
        Stopwatch timer,
        long copiedBase,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDir);
        var copied = copiedBase;
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            await CopyFileAsync(file, Path.Combine(targetDir, fileName), overwrite: true, direction, progress, timer, copied, totalBytes, cancellationToken);
            copied += new FileInfo(file).Length;
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dirName = Path.GetFileName(dir);
            await CopyDirectoryAsync(dir, Path.Combine(targetDir, dirName), direction, progress, timer, copied, totalBytes, cancellationToken);
        }
    }
}
