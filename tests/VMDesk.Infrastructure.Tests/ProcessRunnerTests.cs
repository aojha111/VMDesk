using System.Diagnostics;
using VMDesk.Infrastructure.Discovery;
using Xunit;

namespace VMDesk.Infrastructure.Tests;

/// <summary>
/// Real child-process behaviour: stdout/stderr capture without deadlock, non-zero exits
/// returned (not thrown), and cancellation killing the child.
/// </summary>
public sealed class ProcessRunnerTests
{
    private static readonly string Cmd = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    [Fact]
    public async Task Captures_stdout_and_zero_exit()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(Cmd, "/c echo hello", CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.StdOut.Trim());
    }

    [Fact]
    public async Task Non_zero_exit_is_returned_not_thrown()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(Cmd, "/c exit 3", CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task Captures_stderr_alongside_stdout()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(Cmd, "/c echo out & echo oops 1>&2", CancellationToken.None);

        Assert.Contains("out", result.StdOut);
        Assert.Contains("oops", result.StdErr);
    }

    [Fact]
    public async Task Large_output_beyond_pipe_buffers_does_not_deadlock()
    {
        // ~64 KB of stdout plus stderr: serial reads would deadlock the pipes.
        var runner = new ProcessRunner();
        const int lines = 2000;

        var result = await runner.RunAsync(Cmd,
            $"/c for /L %i in (1,1,{lines}) do @echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var lineCount = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(lines, lineCount);
    }

    [Fact]
    public async Task Cancellation_kills_the_child_and_throws_OperationCanceled()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource(300);
        var started = Stopwatch.StartNew();

        // ping runs for 30 seconds; the runner must abort within ~a second of cancellation.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(Cmd, "/c ping 127.0.0.1 -n 30 > nul", cts.Token));

        started.Stop();
        Assert.True(started.ElapsedMilliseconds < 5000, $"cancellation took {started.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Missing_executable_throws_instead_of_hanging()
    {
        var runner = new ProcessRunner();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            runner.RunAsync(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-tool-vmview.exe"), "list", CancellationToken.None));
    }
}
