using System.Text.RegularExpressions;
using VMDesk.Application.Services;
using VMDesk.Core.Interfaces;

namespace VMDesk.Infrastructure.Discovery;

/// <summary>
/// Discovers VirtualBox VMs via VBoxManage.exe: <c>list vms</c> for the inventory,
/// <c>showvminfo --machinereadable</c> for the power state and <c>guestproperty/get</c>
/// best-effort for the guest IP. The executable is located through
/// %VBOX_INSTALL_PATH%/%VBOX_MSI_INSTALL_PATH% and the default install folder.
/// </summary>
public sealed class VirtualBoxDiscoveryProvider : IVmDiscoveryProvider
{
    private const string GuestIpProperty = "/VirtualBox/GuestInfo/Net/0/V4/IP";

    private static readonly Regex ListLine = new(
        "\\\"(?<name>[^\\\"]+)\\\"\\s*\\{(?<id>[^}]+)\\}",
        RegexOptions.Compiled);

    private static readonly Regex StateLine = new(
        "^VM State=\\\"(?<state>[^\\\"]*)\\\"",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly IProcessRunner _runner;
    private readonly IAppLog _log;

    public VirtualBoxDiscoveryProvider(IProcessRunner runner, IAppLogFactory logFactory)
    {
        _runner = runner;
        _log = logFactory.GetLogger("Discovery.VirtualBox");
    }

    public string ProviderName => "VirtualBox";

    public string? UnavailableReason { get; private set; }

    public bool IsAvailable()
    {
        UnavailableReason = null;
        if (FindVBoxManage() is null)
        {
            UnavailableReason = "VirtualBox was not found: VBoxManage.exe is missing from "
                + "%VBOX_INSTALL_PATH%, %VBOX_MSI_INSTALL_PATH% and C:\\Program Files\\Oracle\\VirtualBox.";
            return false;
        }

        return true;
    }

    public Task<IReadOnlyList<DiscoveredVm>> DiscoverAsync(CancellationToken ct)
    {
        var executable = FindVBoxManage();
        if (executable is null)
        {
            IsAvailable(); // records the note
            return Task.FromResult<IReadOnlyList<DiscoveredVm>>(Array.Empty<DiscoveredVm>());
        }

        return DiscoverWithExecutableAsync(executable, ct);
    }

    /// <summary>Scan body with the located VBoxManage.exe; public so tests can inject a fake path.</summary>
    public async Task<IReadOnlyList<DiscoveredVm>> DiscoverWithExecutableAsync(string vboxManage, CancellationToken ct)
    {
        UnavailableReason = null;

        List<DiscoveredVm> machines;
        try
        {
            var list = await _runner.RunAsync(vboxManage, "list vms", ct).ConfigureAwait(false);
            if (list.ExitCode != 0)
            {
                UnavailableReason = "VBoxManage list vms failed: " + FirstLine(list.StdErr);
                _log.Warn(UnavailableReason);
                return Array.Empty<DiscoveredVm>();
            }

            machines = ParseList(list.StdOut).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UnavailableReason = "Could not run VBoxManage: " + ex.Message;
            _log.Warn(UnavailableReason);
            return Array.Empty<DiscoveredVm>();
        }

        var result = new List<DiscoveredVm>(machines.Count);
        foreach (var vm in machines)
        {
            ct.ThrowIfCancellationRequested();
            var state = await ReadStateAsync(vboxManage, vm.Name, ct).ConfigureAwait(false);
            var guestIp = await ReadGuestIpAsync(vboxManage, vm.Name, ct).ConfigureAwait(false);
            result.Add(vm with { PowerState = state, Host = guestIp });
        }

        return result;
    }

    /// <summary>Parses <c>VBoxManage list vms</c>: one <c>"Name" {uuid}</c> per line; state is filled in later.</summary>
    public static IReadOnlyList<DiscoveredVm> ParseList(string output)
    {
        var result = new List<DiscoveredVm>();
        foreach (Match match in ListLine.Matches(output ?? string.Empty))
        {
            result.Add(new DiscoveredVm("VirtualBox", match.Groups["id"].Value.Trim(),
                match.Groups["name"].Value, null, "Unknown"));
        }

        return result;
    }

    /// <summary>Extracts <c>VM State="..."</c> from machinereadable showvminfo output.</summary>
    public static string ParseState(string showVmInfoOutput)
    {
        var match = StateLine.Match(showVmInfoOutput ?? string.Empty);
        return match.Success ? MapVBoxState(match.Groups["state"].Value) : "Unknown";
    }

    private static string MapVBoxState(string vboxState) => vboxState.ToLowerInvariant() switch
    {
        "running" => "Running",
        "powered off" => "Off",
        "saved" or "paused" => "Paused",
        _ => "Unknown",
    };

    /// <summary>
    /// Reads a guestproperty/get answer: <c>Value: x</c> or <c>property = x</c>;
    /// unset properties and noise yield null.
    /// </summary>
    public static string? ParseGuestIp(string guestPropertyOutput)
    {
        foreach (var rawLine in (guestPropertyOutput ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            string? value = null;
            if (line.StartsWith("Value:", StringComparison.OrdinalIgnoreCase))
            {
                value = line["Value:".Length..].Trim();
            }
            else if (line.Contains(" = ", StringComparison.Ordinal))
            {
                value = line[(line.IndexOf(" = ", StringComparison.Ordinal) + 3)..].Trim();
            }

            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    private static string? FindVBoxManage()
    {
        foreach (var variable in new[] { "VBOX_INSTALL_PATH", "VBOX_MSI_INSTALL_PATH" })
        {
            foreach (var directory in (Environment.GetEnvironmentVariable(variable) ?? string.Empty)
                         .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(directory, "VBoxManage.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        const string defaultInstall = @"C:\Program Files\Oracle\VirtualBox\VBoxManage.exe";
        return File.Exists(defaultInstall) ? defaultInstall : null;
    }

    private async Task<string> ReadStateAsync(string vboxManage, string name, CancellationToken ct)
    {
        try
        {
            var info = await _runner.RunAsync(vboxManage, $"showvminfo {QuoteArg(name)} --machinereadable", ct)
                .ConfigureAwait(false);
            return info.ExitCode == 0 ? ParseState(info.StdOut) : "Unknown";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"showvminfo for '{name}' failed; power state stays Unknown: {ex.Message}");
            return "Unknown";
        }
    }

    private async Task<string?> ReadGuestIpAsync(string vboxManage, string name, CancellationToken ct)
    {
        try
        {
            var guest = await _runner.RunAsync(vboxManage, $"guestproperty/get {QuoteArg(name)} {GuestIpProperty}", ct)
                .ConfigureAwait(false);
            return guest.ExitCode == 0 ? ParseGuestIp(guest.StdOut) : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"guestproperty for '{name}' failed; host stays null: {ex.Message}");
            return null;
        }
    }

    private static string QuoteArg(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
