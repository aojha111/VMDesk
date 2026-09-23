using VMDesk.Application.Services;
using VMDesk.Infrastructure.Discovery;
using Xunit;

namespace VMDesk.Infrastructure.Tests;

/// <summary>
/// Canned-output parser tests for the discovery providers (Task 6). Parsers are pure and
/// public static so they can be proven without Hyper-V/VirtualBox/VMware installed.
/// </summary>
public sealed class DiscoveryParserTests
{
    // ---------- VirtualBox: VBoxManage list vms ----------

    [Fact]
    public void Parses_vbox_list_and_state()
    {
        var vms = VirtualBoxDiscoveryProvider.ParseList(
            "\"Win11-Test\" {b482f1d1-1111-2222-3333-444455556666}\n\"Ubuntu Server\" {aa111111-2222-3333-4444-555566667777}");

        Assert.Equal(2, vms.Count);
        Assert.Equal("Win11-Test", vms[0].Name);
        Assert.Equal("b482f1d1-1111-2222-3333-444455556666", vms[0].ProviderId);
        Assert.Equal("VirtualBox", vms[0].Provider);
        // The list alone has no power state; showvminfo fills it in later.
        Assert.Equal("Unknown", vms[0].PowerState);
        Assert.Null(vms[0].Host);
        Assert.Equal("Ubuntu Server", vms[1].Name);
    }

    [Fact]
    public void VBox_list_ignores_header_and_malformed_lines()
    {
        Assert.Empty(VirtualBoxDiscoveryProvider.ParseList("No installed Virtual machines."));
        Assert.Empty(VirtualBoxDiscoveryProvider.ParseList(""));
        var vms = VirtualBoxDiscoveryProvider.ParseList("junk line\n\"Only\" {11111111-2222-3333-4444-555555555555}");
        Assert.Single(vms);
    }

    [Theory]
    [InlineData("VM State=\"running\"", "Running")]
    [InlineData("VM State=\"powered off\"", "Off")]
    [InlineData("VM State=\"saved\"", "Paused")]
    [InlineData("VM State=\"aborted\"", "Unknown")]
    [InlineData("name=\"Win11-Test\"\nno state here", "Unknown")]
    public void Parses_vbox_showvminfo_state(string output, string expected)
    {
        Assert.Equal(expected, VirtualBoxDiscoveryProvider.ParseState(output));
    }

    [Theory]
    [InlineData("Value: 10.0.2.15", "10.0.2.15")]
    [InlineData("/VirtualBox/GuestInfo/Net/0/V4/IP = 192.168.1.44", "192.168.1.44")]
    [InlineData("No value set for property '/VirtualBox/GuestInfo/Net/0/V4/IP'.", null)]
    [InlineData("", null)]
    public void Parses_vbox_guest_ip(string output, string? expected)
    {
        Assert.Equal(expected, VirtualBoxDiscoveryProvider.ParseGuestIp(output));
    }

    // ---------- Hyper-V: PowerShell Get-CimInstance JSON projection ----------

    [Fact]
    public void HyperV_machines_json_skips_parent_partition()
    {
        const string json = """
            [{"Name":"Win11-Test","Id":"00019C60-000F-000B-0000-000000000000","EnabledState":2,"Caption":"Virtual Machine"},
             {"Name":"HOST-OJHA","Id":"5400FF32-1111-2222-3333-444455556666","EnabledState":3,"Caption":"Microsoft Computer System"}]
            """;

        var vms = HyperVDiscoveryProvider.ParseMachines(json);

        Assert.Single(vms);
        Assert.Equal("Win11-Test", vms[0].Name);
        Assert.Equal("00019C60-000F-000B-0000-000000000000", vms[0].ProviderId);
        Assert.Equal("HyperV", vms[0].Provider);
        Assert.Equal("Running", vms[0].PowerState);
    }

    [Fact]
    public void HyperV_machines_json_handles_single_object_and_empty_input()
    {
        // ConvertTo-Json emits a bare object (not an array) when only one item flows through.
        var vms = HyperVDiscoveryProvider.ParseMachines(
            """{"Name":"Only","Id":"id-1","EnabledState":3,"Caption":"Virtual Machine"}""");

        Assert.Single(vms);
        Assert.Equal("Off", vms[0].PowerState);

        Assert.Empty(HyperVDiscoveryProvider.ParseMachines(""));
        Assert.Empty(HyperVDiscoveryProvider.ParseMachines("   "));
        Assert.Empty(HyperVDiscoveryProvider.ParseMachines("null"));
    }

    [Theory]
    [InlineData(2, "Running")]
    [InlineData(3, "Off")]
    [InlineData(4, "Off")]
    [InlineData(10, "Paused")]
    [InlineData(11, "Paused")]
    [InlineData(6, "Unknown")]
    [InlineData(99, "Unknown")]
    public void HyperV_enabled_state_maps_to_power_state(int enabledState, string expected)
    {
        var json = $$"""[{"Name":"VM","Id":"x","EnabledState":{{enabledState}},"Caption":"Virtual Machine"}]""";

        var vms = HyperVDiscoveryProvider.ParseMachines(json);

        Assert.Equal(expected, Assert.Single(vms).PowerState);
    }

    [Fact]
    public void HyperV_machine_without_id_falls_back_to_name_as_provider_id()
    {
        var vms = HyperVDiscoveryProvider.ParseMachines(
            """[{"Name":"NoId","EnabledState":2,"Caption":"Virtual Machine"}]""");

        Assert.Equal("NoId", Assert.Single(vms).ProviderId);
    }

    [Fact]
    public void HyperV_kvp_hosts_prefer_rdp_connect_host_then_ip_address()
    {
        const string json = """
            [{"Name":"Win11-Test","Items":[
                {"Key":"IPAddress","Value":"10.0.0.7,fe80::1"},
                {"Key":"OSName","Value":"Windows 11 Pro"},
                {"Key":"com.microsoft.rdp.connect.host","Value":"10.20.30.40"}]},
             {"Name":"Only","Items":[{"Key":"Backup IPAddress","Value":"172.16.0.9"}]},
             {"Name":"Bare","Items":[]}]
            """;

        var hosts = HyperVDiscoveryProvider.ParseKvpHosts(json);

        Assert.Equal("10.20.30.40", hosts["Win11-Test"]);
        Assert.Equal("172.16.0.9", hosts["Only"]);
        Assert.False(hosts.ContainsKey("Bare"));
    }

    [Fact]
    public void HyperV_kvp_hosts_handles_single_component_and_garbage()
    {
        var hosts = HyperVDiscoveryProvider.ParseKvpHosts(
            """{"Name":"V","Items":[{"Key":"com.microsoft.rdp.connect.host","Value":"1.2.3.4"}]}""");
        Assert.Equal("1.2.3.4", hosts["V"]);

        Assert.Empty(HyperVDiscoveryProvider.ParseKvpHosts(""));
        Assert.Empty(HyperVDiscoveryProvider.ParseKvpHosts("not json"));
    }

    // ---------- VMware: vmrun list ----------

    [Fact]
    public void Parses_vmrun_list_of_running_vms()
    {
        const string output = """
            Total registered VMs: 2
            Containers running on this machine:
                C:\VMs\Windows 11\Windows 11.vmx
                D:\VMs\Dev.vmx
            """;

        var vms = VMwareDiscoveryProvider.ParseVmrunList(output);

        Assert.Equal(2, vms.Count);
        Assert.Equal("Windows 11", vms[0].Name);
        Assert.Equal(@"C:\VMs\Windows 11\Windows 11.vmx", vms[0].ProviderId);
        Assert.Equal("VMware", vms[0].Provider);
        Assert.Equal("Running", vms[0].PowerState);
        Assert.Null(vms[0].Host);
        Assert.Equal("Dev", vms[1].Name);
    }

    [Fact]
    public void Vmrun_list_with_none_running_yields_no_vms()
    {
        Assert.Empty(VMwareDiscoveryProvider.ParseVmrunList("Total registered VMs: 0\r\nContainers running on this machine:"));
        Assert.Empty(VMwareDiscoveryProvider.ParseVmrunList(""));
    }
}
