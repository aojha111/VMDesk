using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>No-op update service for v1 (spec §55). Architecture only; no server.</summary>
public sealed class NoUpdateService : IUpdateService
{
    private readonly IAppLog _log;

    public NoUpdateService(IAppLogFactory logFactory)
    {
        _log = logFactory.GetLogger("Update");
    }

    public Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        _log.Info("Update check requested; no update service is configured.");
        return Task.FromResult(new UpdateCheckResult(false, "VMDesk 1.0 does not include an update service. Check https://github.com/vmdesk for new releases."));
    }
}
