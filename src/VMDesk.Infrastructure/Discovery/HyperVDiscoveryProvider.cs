using System.Text.Json;
using VMDesk.Application.Services;
using VMDesk.Core.Interfaces;

namespace VMDesk.Infrastructure.Discovery;

/// <summary>
/// Discovers Hyper-V virtual machines. System.Management is not in-box on .NET 10 and the plan
/// forbids new NuGet packages in runtime projects, so WMI under root\virtualization\v2 is read
/// through a powershell.exe child running Get-CimInstance with a ConvertTo-Json projection.
/// Spawn cost is ~0.5-1.5 s per call — acceptable for a background scan capped at 15 s.
/// Failures (missing PowerShell, absent namespace, access denied) set
/// <see cref="UnavailableReason"/> and return empty; nothing is thrown at the caller.
/// </summary>
public sealed class HyperVDiscoveryProvider : IVmDiscoveryProvider
{
    private const string RdpConnectHostKey = "com.microsoft.rdp.connect.host";

    // No double quotes inside the scripts: they are embedded in a quoted -Command argument.
    // -ErrorAction Stop makes a missing namespace/access-denied exit the shell with code 1 so
    // that "exit 0 + empty stdout" unambiguously means "no virtual machines".
    private const string MachinesScript =
        "Get-CimInstance -ErrorAction Stop -Namespace root\\virtualization\\v2 -ClassName Msvm_ComputerSystem " +
        "| Select-Object Name,Id,EnabledState,Caption | ConvertTo-Json -Compress";

    private const string KvpScript =
        "Get-CimInstance -ErrorAction Stop -Namespace root\\virtualization\\v2 -ClassName Msvm_KvpExchangeComponent " +
        "| ForEach-Object { $items = @(); foreach ($item in $_.GuestIntrinsicExchangeItems) { " +
        "$items += [PSCustomObject]@{ Key = $item.Name; Value = $item.Value } }; " +
        "[PSCustomObject]@{ Name = $_.SystemName; Items = $items } } | ConvertTo-Json -Compress -Depth 5";

    private readonly IProcessRunner _runner;
    private readonly IAppLog _log;

    public HyperVDiscoveryProvider(IProcessRunner runner, IAppLogFactory logFactory)
    {
        _runner = runner;
        _log = logFactory.GetLogger("Discovery.HyperV");
    }

    public string ProviderName => "HyperV";

    public string? UnavailableReason { get; private set; }

    /// <summary>PowerShell ships with every supported Windows; the authoritative probe is the scan itself.</summary>
    public bool IsAvailable() => File.Exists(PowerShellPath);

    public async Task<IReadOnlyList<DiscoveredVm>> DiscoverAsync(CancellationToken ct)
    {
        UnavailableReason = null;

        List<DiscoveredVm> machines;
        try
        {
            var result = await _runner.RunAsync(PowerShellPath, Command(MachinesScript), ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                UnavailableReason = ClassifyFailure(result.StdErr);
                _log.Warn($"Hyper-V scan failed (exit {result.ExitCode}): {UnavailableReason}");
                return Array.Empty<DiscoveredVm>();
            }

            machines = ParseMachines(result.StdOut).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UnavailableReason = "PowerShell is not available to query Hyper-V: " + ex.Message;
            _log.Warn(UnavailableReason);
            return Array.Empty<DiscoveredVm>();
        }

        if (machines.Count == 0)
        {
            return machines;
        }

        // Best-effort guest IP / RDP host; when absent the UI offers Console mode instead of RDP.
        var hosts = await ReadKvpHostsAsync(ct).ConfigureAwait(false);
        return machines
            .Select(vm => hosts.TryGetValue(vm.Name, out var host) ? vm with { Host = host } : vm)
            .ToList();
    }

    /// <summary>
    /// Parses the <c>Msvm_ComputerSystem</c> JSON projection (array, or bare object when exactly
    /// one row flows through ConvertTo-Json). Rows whose Caption is not "Virtual Machine" — the
    /// parent partition entry — are skipped.
    /// </summary>
    public static IReadOnlyList<DiscoveredVm> ParseMachines(string json)
    {
        var result = new List<DiscoveredVm>();
        foreach (var item in EnumerateRoot(json))
        {
            if (GetString(item, "Caption") != "Virtual Machine")
            {
                continue;
            }

            var name = GetString(item, "Name");
            if (name is null)
            {
                continue;
            }

            var providerId = GetString(item, "Id");
            result.Add(new DiscoveredVm("HyperV", providerId ?? name,
                name, null, MapEnabledState(GetInt(item, "EnabledState"))));
        }

        return result;
    }

    /// <summary>
    /// Maps Msvm_ComputerSystem.EnabledState: 2 = Running, 3/4 = Off (stopped/shutting down),
    /// 10/11 = Paused/Suspended, everything else Unknown.
    /// </summary>
    public static string MapEnabledState(int enabledState) => enabledState switch
    {
        2 => "Running",
        3 or 4 => "Off",
        10 or 11 => "Paused",
        _ => "Unknown",
    };

    /// <summary>
    /// Builds VM name → connect host from the KVP projection: "com.microsoft.rdp.connect.host"
    /// wins; otherwise the first address item (key containing "IPAddress", first of a possibly
    /// comma-separated value). VMs with no usable item are absent from the map.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseKvpHosts(string json)
    {
        var hosts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in EnumerateRoot(json))
        {
            var name = GetString(component, "Name");
            if (name is null)
            {
                continue;
            }

            string? rdpHost = null;
            string? ipAddress = null;
            if (component.TryGetProperty("Items", out var items))
            {
                foreach (var entry in Enumerate(items))
                {
                    var key = GetString(entry, "Key");
                    var value = GetString(entry, "Value");
                    if (key is null || string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    if (key == RdpConnectHostKey)
                    {
                        rdpHost ??= value;
                    }
                    else if (ipAddress is null && key.Contains("ipaddress", StringComparison.OrdinalIgnoreCase))
                    {
                        // The value can list IPv6 and IPv4 addresses comma-separated; take the first.
                        var first = value.Split(',')[0].Trim();
                        if (first.Length > 0)
                        {
                            ipAddress = first;
                        }
                    }
                }
            }

            var host = rdpHost ?? ipAddress;
            if (host is not null)
            {
                hosts[name] = host;
            }
        }

        return hosts;
    }

    private static string PowerShellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");

    private static string Command(string script) =>
        $"-NoProfile -NonInteractive -Command \"{script}\"";

    private static string ClassifyFailure(string stdErr)
    {
        var firstLine = stdErr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        if (firstLine.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || firstLine.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase)
            || firstLine.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            return "Hyper-V WMI needs elevation — run VMDesk as administrator to list Hyper-V VMs.";
        }

        return "Hyper-V WMI namespace root\\virtualization\\v2 is unavailable"
            + (firstLine.Length == 0 ? "." : $" ({firstLine})");
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadKvpHostsAsync(CancellationToken ct)
    {
        try
        {
            var result = await _runner.RunAsync(PowerShellPath, Command(KvpScript), ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                // Older guests / missing integration services: hosts stay null. Not fatal for the scan.
                _log.Debug("Hyper-V KVP query returned no data; hosts stay null.");
                return EmptyHosts;
            }

            return ParseKvpHosts(result.StdOut);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"Hyper-V KVP query failed; leaving hosts empty: {ex.Message}");
            return EmptyHosts;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyHosts =
        new Dictionary<string, string>();

    private static IEnumerable<JsonElement> EnumerateRoot(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
        {
            return Array.Empty<JsonElement>();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Defensive: a projection we cannot read yields no rows, it never throws at callers.
            return Array.Empty<JsonElement>();
        }

        using (doc)
        {
            var items = new List<JsonElement>();
            foreach (var element in Enumerate(doc.RootElement))
            {
                items.Add(element.Clone());
            }

            return items;
        }
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray();
        }

        return element.ValueKind == JsonValueKind.Object
            ? new[] { element }
            : Array.Empty<JsonElement>();
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

    private static int GetInt(JsonElement element, string property)
    {
        // EnabledState may arrive as a number or, defensively, a numeric string.
        if (element.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return -1;
    }
}
