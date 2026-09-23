using System.Diagnostics;

namespace VMDesk.Infrastructure.Discovery;

/// <summary>Raw outcome of a child-process run. Non-zero exit codes are data, not errors.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Child-process seam used by the discovery providers (injectable for tests).</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <c>file args</c> hidden, capturing stdout/stderr. Never throws on a non-zero exit —
    /// the caller decides what that means. Cancellation kills the child and throws
    /// <see cref="OperationCanceledException"/>; a missing/unlaunchable executable throws
    /// (typically <see cref="System.ComponentModel.Win32Exception"/>).
    /// </summary>
    Task<ProcessResult> RunAsync(string file, string args, CancellationToken ct);
}

/// <summary>Default <see cref="IProcessRunner"/> over <see cref="Process"/>.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string file, string args, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();

        // Start both async reads before waiting for exit: reading the streams only after
        // WaitForExit deadlocks as soon as the child fills a 4 KB pipe buffer.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var registration = ct.Register(() => TryKill(process));

        try
        {
            ct.ThrowIfCancellationRequested();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            // Observe the abandoned readers so a closed stream cannot surface as an
            // unobserved task exception at finalisation.
            _ = stdout.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            _ = stderr.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited — nothing to kill.
        }
        catch (NotSupportedException)
        {
            // Process information unavailable (race with natural exit).
        }
    }
}
