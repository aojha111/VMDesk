using VMDesk.Application.Services;
using VMDesk.Core.Interfaces;
using VMDesk.Infrastructure.Discovery;
using Xunit;

namespace VMDesk.Infrastructure.Tests;

/// <summary>
/// Provider behaviour against a scripted IProcessRunner: availability notes on failure,
/// best-effort enrichment, and the rule that a provider must never throw at its caller.
/// </summary>
public sealed class DiscoveryProviderTests
{
    /// <summary>Returns canned results by matching a substring of the command line.</summary>
    private sealed class ScriptedRunner : IProcessRunner
    {
        public List<string> Calls { get; } = new();
        private readonly List<(string Match, Func<string, ProcessResult> Result)> _script = new();

        public ScriptedRunner When(string match, Func<string, ProcessResult> result)
        {
            _script.Add((match, result));
            return this;
        }

        public ScriptedRunner Returns(string match, int exitCode, string stdout, string stderr = "") =>
            When(match, _ => new ProcessResult(exitCode, stdout, stderr));

        public Task<ProcessResult> RunAsync(string file, string args, CancellationToken ct)
        {
            Calls.Add(file + " " + args);
            foreach (var (match, result) in _script)
            {
                if (args.Contains(match, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(result(args));
                }
            }

            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }

    // ---------- Hyper-V ----------

    [Fact]
    public async Task HyperV_unavailable_namespace_reports_note_and_returns_empty()
    {
        var runner = new ScriptedRunner().Returns("Msvm_ComputerSystem", exitCode: 1,
            stdout: "", stderr: "Get-CimInstance : A parameter cannot be found that matches parameter name 'namespace'.");
        var provider = new HyperVDiscoveryProvider(runner, TestSupport.Logs());

        var vms = await provider.DiscoverAsync(CancellationToken.None);

        Assert.Empty(vms);
        Assert.NotNull(provider.UnavailableReason);
        Assert.Contains("Hyper-V", provider.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HyperV_access_denied_suggests_running_as_administrator()
    {
        var runner = new ScriptedRunner().Returns("Msvm_ComputerSystem", exitCode: 1,
            stdout: "", stderr: "Get-CimInstance : Access denied ");
        var provider = new HyperVDiscoveryProvider(runner, TestSupport.Logs());

        var vms = await provider.DiscoverAsync(CancellationToken.None);

        Assert.Empty(vms);
        Assert.Contains("administrator", provider.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HyperV_discovers_vms_and_merges_best_effort_hosts()
    {
        const string machines =
            """[{"Name":"Win11-Test","Id":"vm-1","EnabledState":2,"Caption":"Virtual Machine"},{"Name":"NoHost","Id":"vm-2","EnabledState":3,"Caption":"Virtual Machine"}]""";
        const string kvp =
            """[{"Name":"Win11-Test","Items":[{"Key":"com.microsoft.rdp.connect.host","Value":"10.20.30.40"}]}]""";
        var runner = new ScriptedRunner()
            .Returns("Msvm_ComputerSystem", 0, machines)
            .Returns("Msvm_KvpExchangeComponent", 0, kvp);
        var provider = new HyperVDiscoveryProvider(runner, TestSupport.Logs());

        var vms = await provider.DiscoverAsync(CancellationToken.None);

        Assert.Null(provider.UnavailableReason);
        Assert.Equal(2, vms.Count);
        Assert.Equal("10.20.30.40", vms[0].Host);
        Assert.Null(vms[1].Host); // no KVP data: Host stays null and the UI offers Console mode
    }

    [Fact]
    public async Task HyperV_survives_kvp_query_failure_without_losing_vms()
    {
        const string machines = """[{"Name":"Win11-Test","Id":"vm-1","EnabledState":2,"Caption":"Virtual Machine"}]""";
        var runner = new ScriptedRunner()
            .Returns("Msvm_ComputerSystem", 0, machines)
            .When("Msvm_KvpExchangeComponent", _ => throw new InvalidOperationException("kvp blew up"));
        var provider = new HyperVDiscoveryProvider(runner, TestSupport.Logs());

        var vms = await provider.DiscoverAsync(CancellationToken.None);

        Assert.Equal("Win11-Test", Assert.Single(vms).Name);
        Assert.Null(vms[0].Host);
    }

    [Fact]
    public async Task HyperV_missing_powershell_is_captured_as_note_not_thrown()
    {
        var runner = new ScriptedRunner()
            .When("Msvm_ComputerSystem", _ => throw new System.ComponentModel.Win32Exception(2, "Cannot find file"));
        var provider = new HyperVDiscoveryProvider(runner, TestSupport.Logs());

        var vms = await provider.DiscoverAsync(CancellationToken.None);

        Assert.Empty(vms);
        Assert.Contains("PowerShell", provider.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- VirtualBox ----------

    [Fact]
    public async Task VBox_full_pipeline_fills_state_and_guest_ip()
    {
        var runner = new ScriptedRunner()
            .Returns("list vms", 0, "\"Win11-Test\" {b482f1d1-1111-2222-3333-444455556666}")
            .Returns("showvminfo", 0, "config=/x\nVM State=\"running\"\n")
            .Returns("guestproperty/get", 0, "Value: 10.0.2.15\n");
        var provider = new VirtualBoxDiscoveryProvider(runner, TestSupport.Logs());

        // Inject the executable path so the test does not depend on VirtualBox being installed.
        var vms = await provider.DiscoverWithExecutableAsync(@"C:\Fake\VBoxManage.exe", CancellationToken.None);

        var vm = Assert.Single(vms);
        Assert.Equal("Win11-Test", vm.Name);
        Assert.Equal("Running", vm.PowerState);
        Assert.Equal("10.0.2.15", vm.Host);
    }

    [Fact]
    public async Task VBox_survives_per_vm_failures()
    {
        var runner = new ScriptedRunner()
            .Returns("list vms", 0, "\"A\" {aaaa}\n\"B\" {bbbb}")
            .Returns("showvminfo", 1, "", stderr: "error")
            .Returns("guestproperty", 0, "");
        var provider = new VirtualBoxDiscoveryProvider(runner, TestSupport.Logs());

        var vms = await provider.DiscoverWithExecutableAsync(@"C:\Fake\VBoxManage.exe", CancellationToken.None);

        Assert.Equal(2, vms.Count);
        Assert.All(vms, vm => Assert.Equal("Unknown", vm.PowerState));
        Assert.All(vms, vm => Assert.Null(vm.Host));
    }

    [Fact]
    public void VBox_not_installed_reports_unavailable_with_note()
    {
        var provider = new VirtualBoxDiscoveryProvider(new ProcessRunner(), TestSupport.Logs());
        Assert.False(provider.IsAvailable());
        Assert.Contains("VirtualBox", provider.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- VMware ----------

    [Fact]
    public void VMware_not_installed_reports_unavailable_with_note()
    {
        var provider = new VMwareDiscoveryProvider(new ProcessRunner(), TestSupport.Logs());
        Assert.False(provider.IsAvailable());
        Assert.Contains("VMware", provider.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }
}
