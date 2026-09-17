using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>
/// Lightweight reachability test: DNS resolution + TCP connect (spec §23).
/// TCP success means "network reachable"; it does NOT prove authentication.
/// </summary>
public sealed class ConnectionTester : IConnectionTester
{
    private readonly IAppLog _log;

    public ConnectionTester(IAppLogFactory logFactory)
    {
        _log = logFactory.GetLogger("ConnectionTest");
    }

    public async Task<ConnectionTestResult> TestAsync(string host, int port, TimeSpan timeout)
    {
        bool dnsResolved = false;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            dnsResolved = addresses.Length > 0;
            if (!dnsResolved)
            {
                return new ConnectionTestResult(false, false, false, null, "Unable to resolve host.");
            }

            var started = Stopwatch.StartNew();
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(addresses[0], port, cts.Token);
            var latency = (int)started.ElapsedMilliseconds;
            _log.Info($"TCP test OK {host}:{port} ({latency} ms).");
            return new ConnectionTestResult(true, true, true, latency, null);
        }
        catch (SocketException ex)
        {
            return new ConnectionTestResult(false, dnsResolved, false, null, $"Socket error: {ex.SocketErrorCode}.");
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, dnsResolved, false, null, $"Timed out after {timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, dnsResolved, false, null, ex.Message);
        }
    }

    public async Task<VmOnlineState> ProbeOnlineAsync(string host, int port, TimeSpan timeout)
    {
        var result = await TestAsync(host, port, timeout);
        return result.TcpReachable ? VmOnlineState.Online : VmOnlineState.Offline;
    }
}
