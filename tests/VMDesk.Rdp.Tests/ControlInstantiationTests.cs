using System.Reflection;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;
using Xunit;

namespace VMDesk.Rdp.Tests;

/// <summary>
/// The availability probe must be backed by a real CoCreateInstance so it can
/// never claim an availability that CreateControl cannot deliver, and the
/// session must fail loudly instead of silently no-opping when the ActiveX
/// control is missing or cannot be wired.
/// </summary>
public class ControlInstantiationTests
{
    private static MicrosoftRdpSession NewSession() => new(
        Guid.NewGuid(),
        "probe-vm",
        new NullLogFactory(),
        () => Task.FromResult<CredentialData?>(new CredentialData("probe-ref", "tester", "not-a-real-secret")));

    [Fact]
    public void Probe_agrees_with_real_instantiation()
    {
        // Probe must not claim availability that CreateControl cannot deliver,
        // nor deny availability a registry-free activation can deliver.
        var (available, details) = RdpControlFactory.Probe();
        Assert.False(string.IsNullOrEmpty(details));
        if (!available) return; // unavailable + details is a valid consistent state
        var ex = Record.Exception(() => { var c = RdpControlFactory.CreateControl(); c.Control.Dispose(); });
        Assert.Null(ex); // if probe says yes, instantiation must succeed
    }

    [Fact]
    public void Probe_details_explain_instantiation_failure_when_unavailable()
    {
        var (available, details) = RdpControlFactory.Probe();
        if (available) return; // a machine with a working coclass has nothing to explain
        // Unavailable must be proven by real activation, not just a registry lookup.
        Assert.Contains("instantiat", details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartConnect_without_a_control_fails_loud()
    {
        var session = NewSession();
        var ex = Assert.Throws<VmConnectionException>(() => session.StartConnect());
        Assert.Contains("could not be created", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareControl_never_connects_and_is_safe_to_repeat()
    {
        var session = NewSession();
        session.PrepareControl(); // On this machine creation may fail; the failure is captured, not thrown.
        session.PrepareControl(); // A double prepare must be cheap and must not throw.

        // MSTSCLib is referenced by path (not transitively), so read the client via reflection.
        var client = typeof(MicrosoftRdpSession)
            .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session);
        var connected = client is null
            ? 0
            : Convert.ToInt32(client.GetType().GetProperty("Connected")!.GetValue(client));
        Assert.Equal(0, connected); // PrepareControl must never start a connection.
    }

    [Fact]
    public async Task ConnectAsync_surfaces_a_real_exception_instead_of_hanging()
    {
        var session = NewSession();
        session.Pending.Host = "198.51.100.7"; // TEST-NET-2, never routable.

        var connect = session.ConnectAsync(new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
        var completed = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(30))) == connect;

        Assert.True(completed, "ConnectAsync must fail fast when the ActiveX control cannot be created or wired.");
        await Assert.ThrowsAnyAsync<Exception>(() => connect);
    }
}
