using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;
using Xunit;

namespace VMDesk.Rdp.Tests;

/// <summary>
/// A VM session must always be hosted by the app itself (embedded workspace or
/// the app's own session window). Handing the connection to the external
/// mstsc.exe client is forbidden: it broke the "view from the app only" rule
/// and surfaced as "the VM opens in Windows Remote Desktop".
/// </summary>
public class InAppSessionPolicyTests
{
    private sealed class FakeCredentialStore : ICredentialStore
    {
        public Task SaveCredentialAsync(string reference, string username, string password) => Task.CompletedTask;
        public Task<CredentialData?> GetCredentialAsync(string reference) => Task.FromResult<CredentialData?>(null);
        public Task DeleteCredentialAsync(string reference) => Task.CompletedTask;
        public Task<bool> CredentialExistsAsync(string reference) => Task.FromResult(false);
        public Task<IReadOnlyList<SavedCredential>> GetCredentialsAsync() => Task.FromResult<IReadOnlyList<SavedCredential>>(Array.Empty<SavedCredential>());
        public Task ClearAllCredentialsAsync() => Task.CompletedTask;
        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
    }

    private static MicrosoftRdpEngine Engine(Func<(bool Available, string Details)> probe) =>
        new(new FakeCredentialStore(), new NullLogFactory(), probe);

    private static VirtualMachineEntity RdpVm() => new() { Name = "test-vm", Host = "10.10.1.36" };

    [Fact]
    public async Task Unavailable_control_fails_actionably_instead_of_opening_mstsc()
    {
        var engine = Engine(() => (false, "No supported MsRdpClient COM coclass could be instantiated."));
        var ex = await Assert.ThrowsAsync<VmConnectionException>(() => engine.CreateSessionAsync(RdpVm()));
        Assert.Contains("Remote Desktop ActiveX control", ex.Message);
        Assert.DoesNotContain("mstsc.exe", ex.Message); // must not point the user at the external client
    }

    [Fact]
    public async Task Available_control_returns_the_in_app_session()
    {
        var engine = Engine(() => (true, "ok"));
        var session = await engine.CreateSessionAsync(RdpVm());
        Assert.IsType<MicrosoftRdpSession>(session);
        Assert.False(session.Capabilities.ExternalClient);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task HyperV_console_without_rdp_host_still_uses_its_own_client()
    {
        // The console path is a provider feature, not an RDP fallback: it must keep working
        // even when the RDP control probe says unavailable.
        var vm = new VirtualMachineEntity { Name = "hv", Provider = "HyperV", ProviderId = "S-1-15-2-x", Host = "" };
        var engine = Engine(() => (false, "no coclass"));
        var session = await engine.CreateSessionAsync(vm);
        Assert.IsType<VmConnectExternalSession>(session);
        await session.DisposeAsync();
    }

    [Fact]
    public void No_session_type_launches_mstsc_anymore()
    {
        var offenders = typeof(MicrosoftRdpEngine).Assembly
            .GetTypes()
            .Where(t => t.Name.Contains("Mstsc", StringComparison.OrdinalIgnoreCase) && !t.IsNested)
            .Select(t => t.FullName)
            .ToList();
        Assert.True(offenders.Count == 0, "Types that launch mstsc must not exist: " + string.Join(", ", offenders));
    }
}
