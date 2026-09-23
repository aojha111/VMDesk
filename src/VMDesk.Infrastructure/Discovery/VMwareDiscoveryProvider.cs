using VMDesk.Application.Services;
using VMDesk.Core.Interfaces;

namespace VMDesk.Infrastructure.Discovery;

/// <summary>
/// Discovers VMware Workstation VMs via vmrun.exe <c>list</c> (running guests only),
/// best-effort: no per-VM state or IP queries.
/// </summary>
public sealed class VMwareDiscoveryProvider : IVmDiscoveryProvider
{
    private readonly IProcessRunner _runner;
    private readonly IAppLog _log;

    public VMwareDiscoveryProvider(IProcessRunner runner, IAppLogFactory logFactory)
    {
        _runner = runner;
        _log = logFactory.GetLogger("Discovery.VMware");
    }

    public string ProviderName => "VMware";

    public string? UnavailableReason { get; private set; }

    public bool IsAvailable()
    {
        UnavailableReason = null;
        if (FindVmrun() is null)
        {
            UnavailableReason = "VMware Workstation was not found: vmrun.exe is missing from the default install folders.";
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<DiscoveredVm>> DiscoverAsync(CancellationToken ct)
    {
        var executable = FindVmrun();
        if (executable is null)
        {
            IsAvailable(); // records the note
            return Array.Empty<DiscoveredVm>();
        }

        UnavailableReason = null;
        try
        {
            var result = await _runner.RunAsync(executable, "list", ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var detail = result.StdErr
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault() ?? $"exit {result.ExitCode}";
                UnavailableReason = "vmrun list failed: " + detail;
                _log.Warn(UnavailableReason);
                return Array.Empty<DiscoveredVm>();
            }

            return ParseVmrunList(result.StdOut);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UnavailableReason = "Could not run vmrun: " + ex.Message;
            _log.Warn(UnavailableReason);
            return Array.Empty<DiscoveredVm>();
        }
    }

    /// <summary>
    /// Parses <c>vmrun list</c>: header lines ("Total registered VMs: n", "Containers running on
    /// this machine:") are skipped; each remaining .vmx path is a running VM.
    /// </summary>
    public static IReadOnlyList<DiscoveredVm> ParseVmrunList(string output)
    {
        var result = new List<DiscoveredVm>();
        foreach (var rawLine in (output ?? string.Empty).Split('\n'))
        {
            var path = rawLine.Trim();
            if (path.EndsWith(".vmx", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new DiscoveredVm("VMware", path,
                    Path.GetFileNameWithoutExtension(path), null, "Running"));
            }
        }

        return result;
    }

    private static string? FindVmrun()
    {
        var roots = new List<string>();
        foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var value = variable == "ProgramFiles"
                ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                : Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
            {
                roots.Add(value);
            }
        }

        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, @"VMware\VMware Workstation\vmrun.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
