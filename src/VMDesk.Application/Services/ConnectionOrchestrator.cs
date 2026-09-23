using System.Diagnostics;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>
/// Per-VM connection timeout and retry orchestration (spec §82-84).
/// Reports elapsed seconds while connecting, supports cancel, retries with delay.
/// </summary>
public sealed class ConnectionOrchestrator : IConnectionOrchestrator
{
    private readonly IAppLog _log;

    public ConnectionOrchestrator(IAppLogFactory logFactory)
    {
        _log = logFactory.GetLogger("Connect");
    }

    public async Task ConnectAsync(
        IRemoteSession session,
        VirtualMachineEntity vm,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp(vm.ConnectionTimeoutSeconds, 5, 600);
        var retryCount = Math.Max(0, vm.ConnectionRetryCount);
        var retryDelaySeconds = Math.Max(0, vm.ConnectionRetryDelaySeconds);

        var attempts = retryCount + 1;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (attempt > 1)
            {
                progress?.Report($"Retrying ({attempt}/{attempts}) in {retryDelaySeconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
            }

            try
            {
                var started = Stopwatch.StartNew();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var elapsedTimer = Task.Run(async () =>
                {
                    while (!timeoutCts.IsCancellationRequested)
                    {
                        await Task.Delay(1000, CancellationToken.None);
                        progress?.Report($"Elapsed: {(int)started.Elapsed.TotalSeconds}s / {timeoutSeconds}s");
                    }
                }, CancellationToken.None);

                try
                {
                    await session.ConnectAsync(timeoutCts.Token);
                }
                finally
                {
                    timeoutCts.Cancel();
                    try { await elapsedTimer; } catch { /* timer cancelled */ }
                }

                if (session.State == Core.Enums.ConnectionState.Connected)
                {
                    _log.Info($"Connected to '{vm.Name}' after {started.Elapsed.TotalSeconds:0.0}s (attempt {attempt}).");
                    return;
                }

                if (session.State == Core.Enums.ConnectionState.Connecting)
                {
                    throw new TimeoutException($"Connection timed out after {timeoutSeconds} seconds.");
                }

                throw new InvalidOperationException(session.LastError ?? "Connection failed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _log.Info($"Connection to '{vm.Name}' cancelled by user.");
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-attempt timeout fired, not a user cancel: session.ConnectAsync
                // surfaces it as TaskCanceledException on the timeout token.
                lastError = new TimeoutException($"Connection timed out after {timeoutSeconds} seconds.");
                _log.Warn($"Attempt {attempt}/{attempts} to '{vm.Name}' timed out.");
                await TryCancelPendingConnect(session);
            }
            catch (TimeoutException)
            {
                lastError = new TimeoutException($"Connection timed out after {timeoutSeconds} seconds.");
                _log.Warn($"Attempt {attempt}/{attempts} to '{vm.Name}' timed out.");
                await TryCancelPendingConnect(session);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _log.Warn($"Attempt {attempt}/{attempts} to '{vm.Name}' failed: {ConnectionErrors.Sanitize(ex)}");
                await TryCancelPendingConnect(session);
            }
        }

        var message = ConnectionErrors.ToFriendly(lastError, vm);
        throw new VmConnectionException(message, lastError);
    }

    private async Task TryCancelPendingConnect(IRemoteSession session)
    {
        try
        {
            await session.DisconnectAsync();
        }
        catch (Exception ex)
        {
            // Best effort: a dead handle must not mask the real connect error.
            _log.Debug("Best-effort cancel after failed attempt: " + ex.Message);
        }
    }
}
