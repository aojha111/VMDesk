using VMDesk.Core.Entities;
using VMDesk.Rdp;
using Xunit;

namespace VMDesk.Rdp.Tests;

/// <summary>
/// The port is optional: 0 (or leaving the Add VM field empty) means "use the
/// RDP default port (3389)" — the ActiveX control's own default must not be
/// touched in that case, and a custom port must still be forwarded.
/// </summary>
public class OptionalPortTests
{
    [Fact]
    public void ApplyPending_leaves_port_at_zero_when_no_custom_port_is_set()
    {
        var vm = new VirtualMachineEntity { Host = "box", Port = 0 };
        var pending = new PendingOptions();

        MicrosoftRdpEngine.ApplyPending(pending, vm);

        Assert.Equal(0, pending.Port);
        Assert.Equal("box", pending.Host);
    }

    [Fact]
    public void ApplyPending_forwards_a_custom_port()
    {
        var vm = new VirtualMachineEntity { Host = "box", Port = 3390 };
        var pending = new PendingOptions();

        MicrosoftRdpEngine.ApplyPending(pending, vm);

        Assert.Equal(3390, pending.Port);
    }

    [Fact]
    public void HostDisplay_omits_the_port_when_it_is_not_set()
    {
        var vm = new VirtualMachineEntity { Host = "box", Port = 0 };

        Assert.Equal("box", vm.HostDisplay);
    }

    [Fact]
    public void HostDisplay_shows_a_custom_port()
    {
        var vm = new VirtualMachineEntity { Host = "box", Port = 3390 };

        Assert.Equal("box:3390", vm.HostDisplay);
    }
}
