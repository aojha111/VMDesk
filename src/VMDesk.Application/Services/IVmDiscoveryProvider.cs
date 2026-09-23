namespace VMDesk.Application.Services;

/// <summary>
/// One hypervisor discovery source (spec §27). Lives in the Application layer because the
/// DTO it produces, <see cref="DiscoveredVm"/>, is defined with the Task 5 sync service;
/// Core cannot reference Application.
/// <para>
/// Contract: implementations must never throw at their caller. Any failure — missing tool,
/// access denied, parse error — is reported by returning an empty list and setting
/// <see cref="UnavailableReason"/>; <see cref="DiscoverAsync"/> must honour the
/// cancellation token (the aggregator caps each run at 15 s).
/// </para>
/// </summary>
public interface IVmDiscoveryProvider
{
    /// <summary>Catalog <c>Provider</c> value this source writes, e.g. "HyperV".</summary>
    string ProviderName { get; }

    /// <summary>
    /// Cheap synchronous probe (tool/env presence, no process spawn on the happy path).
    /// When it returns false it must also set <see cref="UnavailableReason"/>.
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Why the provider cannot scan (not installed, needs elevation, timed out…);
    /// null when healthy. Read after <see cref="IsAvailable"/> or <see cref="DiscoverAsync"/>.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>Scans the hypervisor; failures are captured, never thrown (cancellation excluded).</summary>
    Task<IReadOnlyList<DiscoveredVm>> DiscoverAsync(CancellationToken ct);
}
