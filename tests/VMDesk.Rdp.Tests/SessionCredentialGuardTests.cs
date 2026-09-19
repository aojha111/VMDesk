using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using VMDesk.Rdp;
using Xunit;

namespace VMDesk.Rdp.Tests;

/// <summary>Minimal no-op logger for sessions under test.</summary>
internal sealed class NullLog : IAppLog
{
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

internal sealed class NullLogFactory : IAppLogFactory
{
    public IAppLog GetLogger(string category) => new NullLog();
    public void Flush() { }
}

/// <summary>
/// The engine must refuse to build a session that cannot resolve a saved
/// credential, must split DOMAIN\user correctly, and sessions must guard
/// their lifecycle — all without instantiating the ActiveX control.
/// </summary>
public class SessionCredentialGuardTests
{
    private static IAppLogFactory Logs() => new NullLogFactory();

    [Fact]
    public async Task ConnectAsync_without_a_saved_credential_fails_fast_with_guidance()
    {
        var session = new MicrosoftRdpSession(
            Guid.NewGuid(),
            "cred-less VM",
            Logs(),
            credentialResolver: () => Task.FromResult<CredentialData?>(null));

        var exception = await Assert.ThrowsAsync<VmConnectionException>(
            () => session.ConnectAsync(CancellationToken.None));

        Assert.Contains("credential", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, session.RemoteDesktopWidth); // No control was created.
    }

    [Fact]
    public async Task ConnectAsync_when_the_credential_store_fails_never_reports_connected()
    {
        var session = new MicrosoftRdpSession(
            Guid.NewGuid(),
            "broken store",
            Logs(),
            credentialResolver: () => throw new InvalidOperationException("Credential store offline"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConnectAsync(CancellationToken.None));
        Assert.NotEqual(ConnectionState.Connected, session.State);
    }

    [Fact]
    public void ApplyPending_splits_domain_from_username()
    {
        var vm = new VirtualMachineEntity { Host = "box", Port = 3389, Username = "CORP\\alice" };
        var pending = new PendingOptions();

        MicrosoftRdpEngine.ApplyPending(pending, vm);

        Assert.Equal("CORP", pending.Domain);
        Assert.Equal("alice", pending.Username);
        Assert.Equal("box", pending.Host);
        Assert.Equal(3389, pending.Port);
    }

    [Fact]
    public void ApplyPending_keeps_plain_usernames_and_clamps_display_values()
    {
        var vm = new VirtualMachineEntity { Host = "box2", Username = "bob", ScreenWidth = 99999, ScreenHeight = 0 };
        var pending = new PendingOptions();

        MicrosoftRdpEngine.ApplyPending(pending, vm);

        Assert.Equal(string.Empty, pending.Domain);
        Assert.Equal("bob", pending.Username);
        Assert.True(pending.SmartSizing);
        Assert.InRange(pending.ScreenWidth, 640, 8192); // Clamped.
        Assert.InRange(pending.ScreenHeight, 480, 8192);
    }

    [Fact]
    public void ApplyPending_defaults_drives_to_all_when_unspecified()
    {
        var pending = new PendingOptions();
        MicrosoftRdpEngine.ApplyPending(pending, new VirtualMachineEntity());

        Assert.Equal("*", pending.DriveList);
    }

    [Fact]
    public async Task Disconnect_is_safe_before_any_connect()
    {
        var session = new MicrosoftRdpSession(
            Guid.NewGuid(),
            "never connected",
            Logs(),
            credentialResolver: () => Task.FromResult<CredentialData?>(null));

        await session.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, session.State);
    }

    [Fact]
    public async Task Disposed_session_rejects_commands()
    {
        var session = new MicrosoftRdpSession(
            Guid.NewGuid(),
            "disposed",
            Logs(),
            credentialResolver: () => Task.FromResult<CredentialData?>(null));

        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReconnectAsync());
        Assert.Throws<ObjectDisposedException>(session.SendCtrlAltDel);
    }
}
