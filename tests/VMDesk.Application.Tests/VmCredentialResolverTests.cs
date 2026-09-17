using VMDesk.Application.Services;
using Xunit;

namespace VMDesk.Application.Tests;

public sealed class VmCredentialResolverTests
{
    [Fact]
    public void ResolveUsername_prefers_the_selected_saved_credential_name()
    {
        var username = VmCredentialResolver.ResolveUsername("VMDesk/edge", "edge-admin", "manual-user");

        Assert.Equal("edge-admin", username);
    }

    [Fact]
    public void ResolveUsername_uses_manual_entry_only_when_no_saved_credential_is_selected()
    {
        var username = VmCredentialResolver.ResolveUsername(string.Empty, string.Empty, "manual-user");

        Assert.Equal("manual-user", username);
    }
}
