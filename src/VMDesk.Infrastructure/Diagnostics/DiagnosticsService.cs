using System.Net.NetworkInformation;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Infrastructure.Diagnostics;

/// <summary>Diagnostics collection (spec §56). RDP probe is injected to avoid a layering cycle.</summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly Func<string> _databasePath;
    private readonly Func<Task<RdpAvailabilityInfo>> _rdpProbe;
    private readonly Func<Task<bool>> _credentialStoreProbe;
    private readonly IAppLog _log;

    public DiagnosticsService(
        Func<string> databasePath,
        Func<Task<RdpAvailabilityInfo>> rdpProbe,
        Func<Task<bool>> credentialStoreProbe,
        IAppLogFactory logFactory)
    {
        _databasePath = databasePath;
        _rdpProbe = rdpProbe;
        _credentialStoreProbe = credentialStoreProbe;
        _log = logFactory.GetLogger("Diagnostics");
    }

    public async Task<DiagnosticsInfo> CollectAsync()
    {
        var dbPath = _databasePath();
        var dbStatus = File.Exists(dbPath) ? $"OK ({new FileInfo(dbPath).Length / 1024.0:0.0} KB)" : "Not created";
        var credOk = await _credentialStoreProbe();
        var rdp = await _rdpProbe();

        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => $"{n.Name} ({n.NetworkInterfaceType})")
            .ToList();

        return new DiagnosticsInfo(
            AppVersionReader.Read(),
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            rdp.Available ? $"Available ({rdp.ClientVersion ?? "Microsoft RDP ActiveX"})" : $"Not available. {rdp.Details}",
            dbPath,
            dbStatus,
            credOk ? "Available (Windows Credential Manager)" : "Not available",
            adapters.Count > 0 ? string.Join(", ", adapters) : "None up",
            Configuration.AppPaths.LogsDirectory);
    }

    public async Task<string> TestDatabaseAsync()
    {
        try
        {
            var path = _databasePath();
            if (!File.Exists(path))
            {
                return "Database file does not exist yet.";
            }

            await using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var size = fs.Length;
            return $"Database opened OK ({size / 1024.0:0.0} KB).";
        }
        catch (IOException ex)
        {
            return $"Database open failed: {ex.Message}";
        }
    }

    public Task<string> TestCredentialStoreAsync()
    {
        return Task.FromResult("Credential store probed at startup; see status above.");
    }
}
