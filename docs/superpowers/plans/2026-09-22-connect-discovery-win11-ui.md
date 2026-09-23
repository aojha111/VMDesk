# VMDesk Connect, Discovery & Windows 11 UI Overhaul — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make VMDesk actually usable: reliably connect to VMs (embedded ActiveX fixed, mstsc.exe fallback), auto-discover local hypervisor VMs, keep the laptop awake during sessions, and rebuild the UI as a proper Windows 11 Fluent experience with working multi-theme support.

**Architecture:** Four vertical phases on the existing layered solution (`Core` → `Application`/`Rdp`/`Infrastructure` → `App`). Phase A repairs the RDP connect path (registry-free COM instantiation, parent-before-connect ordering, mstsc.exe external session fallback, live cancel). Phase B adds hypervisor discovery providers (Hyper-V WMI, VirtualBox/VMware CLI) synced into the SQLite catalog. Phase C adds power management (`SetThreadExecutionState`) and configurable keep-alive/idle timers. Phase D consolidates theming (Light/Dark/System + accent, dead dictionaries deleted, Mica backdrops, Win11 control styles).

**Tech Stack:** .NET 10 WPF (`net10.0-windows`, x64, self-contained), EF Core 9 + SQLite, MSTSCLib/AxMSTSCLib RCWs (`src/libs`), Win32 interop (no new NuGet runtime deps), xUnit 2.9.

**Spec:** `Master.MD` and `README.md` at repo root; this plan's Evidence section below supersedes where they conflict.

## Global Constraints

- `TreatWarningsAsErrors=true` — every change must build clean (`dotnet build VMDesk.slnx -c Release`).
- No new NuGet package references in runtime projects; use Win32 interop and BCL only.
- Tests live in `tests/VMDesk.Application.Tests` and `tests/VMDesk.Rdp.Tests` (xUnit). Run `dotnet test VMDesk.slnx -c Release` — all suites must stay green.
- UI colors must come from theme token brushes (`WindowBrush`, `SurfaceBrush`, `PrimaryBrush`, …) — no new hardcoded hex outside `Resources/Themes/*.xaml`.
- Logging via `IAppLog`; never log passwords or credential material.
- Commits: conventional style (`fix:`, `feat:`, `build:`), one commit per task.
- DB schema changes go through the existing `DatabaseBootstrapper` pattern (EF migration or `EnsureCreated` fallback already in place; add columns as nullable/safe defaults).

## Evidence (root causes already confirmed — do not re-investigate)

1. **No discovery exists**: `grep Hyper-V|VirtualBox|VMware|WMI` over `src` → only one unrelated match. VMs are manual SQLite rows; user's DB has `VirtualMachines = 0 rows`. Empty list = designed behavior, not a bug — the product lacks the feature.
2. **Probe lies**: `src/VMDesk.Rdp/Interop/RdpControlFactory.cs:68-101` checks registry CLSIDs only. On the dev machine **zero MsTscAx CLSIDs are registered** (HKLM+HKCU) yet `mstscax.dll` exists — the registry check is both a false-negative source (newer Windows registers under ProgramFiles-side paths/Wow64) and a false-positive source (registered CLSID but broken inproc server).
3. **Parent-after-connect**: `MicrosoftRdpSession.ConnectAsync` (`src/VMDesk.Rdp/MicrosoftRdpSession.cs:98-101`) calls `CreateControl()` + `Connect()` **before** `MainWindow.ShowEmbeddedSession` (`src/VMDesk.App/Views/MainWindow.xaml.cs:235-252`) parents the AxHost into `WindowsFormsHost` — re-parenting recreates the HWND and kills the in-flight handshake → "hangs, then timeout" (user-confirmed symptom).
4. **Silent failure paths**: `SafeConnect` no-ops when `_client` null (`MicrosoftRdpSession.Options.cs:115-126`); `Subscribe` swallows wiring failures as Warn (`MicrosoftRdpSession.Events.cs:43-76`); progress `CancelRequested` never wired to a `CancellationTokenSource` (`MainWindow.xaml.cs:162-171` never passes a token; `ConnectionProgressWindow.xaml.cs:62`).
5. **No sleep prevention anywhere** (`SetThreadExecutionState` count = 0 in src). Keep-alive hardcoded `keepAliveInterval = 60000` (`MicrosoftRdpSession.Options.cs:89`).
6. **Theme sprawl**: 6 dead dictionary files (`Resources/Dark.xaml`, `Light.xaml`, `Teal.xaml`, `Ocean.xaml`, `Fluent.xaml`, `WindowsLight/Dark.xaml`) vs the real pair in `Resources/Themes/`; `App.ApplyTheme` (`src/VMDesk.App/App.xaml.cs:24-41`) only swaps index 0; `ThemeMode` enum is System/Light/Dark only.

---

## Phase A — Connection reliability

### Task 1: Registry-free control instantiation + fail-loud ActiveX sessions

**Files:**
- Modify: `src/VMDesk.Rdp/Interop/RdpControlFactory.cs`
- Modify: `src/VMDesk.Rdp/MicrosoftRdpSession.Options.cs` (`CreateAndConfigureControl`, `SafeConnect`)
- Modify: `src/VMDesk.Rdp/MicrosoftRdpSession.Events.cs` (`AttachEvents`, `Subscribe`)
- Test: `tests/VMDesk.Rdp.Tests/ControlInstantiationTests.cs` (create)

**Interfaces:**
- Produces: `RdpControlFactory.CreateControl()` unchanged signature but instantiation-first; `Probe()` returns `(bool Available, string Details)` where Available=true only if a coclass actually instantiates via `CoCreateInstance`.
- Produces: `MicrosoftRdpSession.PrepareControl()` (UI thread; creates + configures + wires events, does **not** call `Connect()`) and `MicrosoftRdpSession.StartConnect()` (UI thread; calls `_client.Connect()`, throws `VmConnectionException` if `_client` is null). Used by Task 2 to enforce parent-before-connect.

- [ ] **Step 1: Write failing test** — on this machine (MsTscAx not registered) a registry-only probe reports unavailable but `CoCreateInstance` for `CLSID MsTscAx.MsTscAx` may still work; test asserts Probe and CreateControl agree:

```csharp
// ControlInstantiationTests.cs
using VMDesk.Rdp.Interop;
using Xunit;
namespace VMDesk.Rdp.Tests;
public class ControlInstantiationTests
{
    [Fact]
    public void Probe_agrees_with_real_instantiation()
    {
        // Probe must not claim availability that CreateControl cannot deliver,
        // nor deny availability a registry-free activation can deliver.
        var (available, details) = RdpControlFactory.Probe();
        Assert.False(string.IsNullOrEmpty(details));
        if (!available) return; // unavailable + details is a valid consistent state
        var ex = Record.Exception(() => { var c = RdpControlFactory.CreateControl(); c.Control.Dispose(); });
        Assert.Null(ex); // if probe says yes, instantiation must succeed
    }
}
```

- [ ] **Step 2: Run — expect FAIL on this machine if registry says yes but CoCreate fails, or pass vacuously; then implement.**

Run: `dotnet test tests/VMDesk.Rdp.Tests -c Release --filter ControlInstantiationTests`

- [ ] **Step 3: Implement instantiation-first probe.** In `RdpControlFactory`, add P/Invoke and rewrite `Probe()`: for each coclass GUID in `WrapperTypeNames` order, `CoCreateInstance(clsid, CLSCTX_INPROC_SERVER)`; success = available (release the pointer immediately). Keep the registry lookup only as *detail text*. In `CreateControl()`, before `Activator.CreateInstance`, keep behavior but catch `COMException` per candidate (already done) and include every candidate's failure in the thrown message.

```csharp
[DllImport("ole32.dll")]
private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
private const uint CLSCTX_INPROC_SERVER = 1;

internal static bool TryInstantiate(Guid clsid)
{
    var riid = IID_IUnknown;
    var hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref riid, out var obj);
    if (hr == 0 && obj != IntPtr.Zero) { Marshal.Release(obj); return true; }
    return false;
}
```

- [ ] **Step 4: Fail loud in the session.** Split `CreateAndConfigureControl` into `PrepareControl()` (same body minus nothing removed except it stays non-connecting — it already doesn't connect) and make `SafeConnect()` throw when `_client is null`:

```csharp
internal void SafeConnect()
{
    if (_client is null)
    {
        var ex = new VmConnectionException("The RDP control could not be created. Run Diagnostics or use an external client.", null);
        _connectTcs?.TrySetException(ex);
        return;
    }
    try { _client.Connect(); }
    catch (COMException ex) { ReportError("Unable to start the RDP connection.", ex.Message); _connectTcs?.TrySetException(new VmConnectionException("Unable to start the RDP connection.", ex)); }
}
```

- [ ] **Step 5: Escalate critical event wiring.** In `AttachEvents`, track subscription results; `OnConnected`, `OnDisconnected`, `OnFatalError` are CRITICAL — if any fails to subscribe, `PrepareControl` throws `VmConnectionException("Could not attach to RDP control events…")` which faults `_connectTcs` (keep existing catch, change its log to Error). Non-critical events stay Warn.

- [ ] **Step 6: Run tests + build.** `dotnet test VMDesk.slnx -c Release` — all green.
- [ ] **Step 7: Commit** `fix: verify RDP control by instantiation and fail loudly instead of hanging`

### Task 2: Parent-before-connect ordering

**Files:**
- Modify: `src/VMDesk.Application/Services/RemoteSessionManager.cs`
- Modify: `src/VMDesk.Core/Interfaces/SessionInterfaces.cs`
- Modify: `src/VMDesk.App/Views/MainWindow.xaml.cs` (ConnectAsync + ShowEmbeddedSession)
- Modify: `src/VMDesk.App/Views/SessionWindow.xaml.cs:41-74`
- Test: `tests/VMDesk.Application.Tests/ParentBeforeConnectTests.cs` (create)

**Interfaces:**
- Consumes: `MicrosoftRdpSession.PrepareControl()` / `StartConnect()` from Task 1.
- Produces: `IRemoteSession.PrepareControl()` + `StartConnect()` (both no-op-safe on implementations that don't need them) and `RemoteSessionManager.ConnectAsync(vm, credentialReference, progress, surfaceReady: Func<IRemoteSession, Task>, cancellationToken)` — `surfaceReady` is invoked after control prep and **must** parent `HostControl` before it returns; `StartConnect` runs after it.

- [ ] **Step 1: Failing test** using a fake session recording call order:

```csharp
// ParentBeforeConnectTests.cs (in VMDesk.Application.Tests; extend the local fake session in TestDoubles.cs to log calls)
[Fact]
public async Task ConnectAsync_surfaces_control_before_starting_connect()
{
    var fake = new RecordingSession();           // records: "prepare","surface","startconnect"
    var mgr = new RemoteSessionManager(new RecordingEngine(fake), new PassThroughOrchestrator(), new NullLogFactory());
    await mgr.ConnectAsync(new VirtualMachineEntity { Name = "x", Host = "h" }, null, null, s => { s.StartConnect(); return Task.CompletedTask; });
    Assert.Equal(new[] { "prepare", "surface", "startconnect" }, fake.Calls.Take(3).ToArray());
}
```

Note: pass `surfaceReady` as the parameter that calls `StartConnect` — the real call order inside `ConnectAsync` must be `PrepareControl()` → host parents control (UI shows surface) → `surfaceReady()` → orchestrator connect. Wire it so the orchestrator does NOT call `session.ConnectAsync` for prepared sessions; give `RecordingSession.ConnectAsync` a no-op that appends `"startconnect"` — assert order is prepare → surface → startconnect.

- [ ] **Step 2: Run, expect fail (missing parameter).**
- [ ] **Step 3: Implement.** In `RemoteSessionManager.ConnectAsync` add optional `Func<IRemoteSession, Task>? surfaceReady = null` before the token; after `SessionAdded` invocation call `session.PrepareControl()`, then `await _ui(...)`-safe parent callback, then `surfaceReady?.Invoke(session)` before `_orchestrator.ConnectAsync`. All UI-thread hopping stays in `MicrosoftRdpSession` (its `_ui.Send` already marshals). In `MainWindow.ConnectAsync`, replace the connect-then-show flow: show a *prepping* embedded surface (`ShowEmbeddedSession` on the freshly created session before connect) for the Embedded mode; for SeparateWindow, construct `SessionWindow` with the prepared control first, `Show()`, then start connect. Keep `OpenSessionSurface` for the already-connected re-activation path only.
- [ ] **Step 4: Adjust `MicrosoftRdpSession.ConnectAsync`** to be safe when called post-preparation: `PrepareControl()` sets a `_prepared` flag; `ConnectAsync` skips re-creating the control if prepared and calls `SafeConnect` directly (still via `OnUiThread`).
- [ ] **Step 5: Full test + build green; commit** `fix: parent the RDP control into its host before starting the connection`

### Task 3: mstsc.exe external-session fallback

**Files:**
- Create: `src/VMDesk.Rdp/MstscExternalSession.cs`
- Create: `src/VMDesk.Rdp/Interop/MstscLocator.cs`
- Modify: `src/VMDesk.Rdp/MicrosoftRdpEngine.cs` (route to fallback when ActiveX unavailable)
- Modify: `src/VMDesk.Core/Enums` (`SessionHostMode` reuse — no new enum needed; external = `Standalone`)
- Test: `tests/VMDesk.Rdp.Tests/MstscArgsTests.cs` (create)

**Interfaces:**
- Produces: `MstscExternalSession : IRemoteSession` — `PrepareControl()` no-op (HostControl=null), `StartConnect()` → launches `mstsc.exe <host[:port]>` via `Process.Start`, State=Connecting, flips to Disconnected on process exit; `ConnectAsync` resolves Connected as soon as the process is alive (mstsc shows its own UI/auth).
- Produces: `MstscLocator.TryGetFullPath(): string?` → `%SystemRoot%\System32\mstsc.exe` existence check.
- Engine behavior: `CheckAvailabilityAsync` unchanged; `MicrosoftRdpEngine.CreateSessionAsync` returns `MstscExternalSession` when `RdpControlFactory.Probe()` unavailable AND `MstscLocator` finds mstsc; logs Info "Falling back to external mstsc.exe session".

- [ ] **Step 1: Failing tests for argument building (pure, no process):**

```csharp
// MstscArgsTests.cs
public class MstscArgsTests
{
    [Theory]
    public static TheoryData<string,int,string> Cases() => new() { { "10.0.0.5", 0, "10.0.0.5" }, { "10.0.0.5", 3390, "10.0.0.5:3390" } };
    [Theory]
    public void Builds_host_argument_unchanged(string host, int port, string expected) =>
        Assert.Equal(expected, MstscExternalSession.BuildTargetArgument(host, port));
    [Fact]
    public void Locator_finds_system32_mstsc() => Assert.NotNull(MstscLocator.TryGetFullPath());
}
```

- [ ] **Step 2: Run, fail.**
- [ ] **Step 3: Implement `MstscExternalSession`.** Launch with `ProcessStartInfo { FileName = mstsc, Arguments = target, UseShellExecute = true }`; `EnableRaisingEvents`, `Exited` → `State = Disconnected` + `StateChanged`. `DisconnectAsync` kills the process if running. `SendCtrlAltDel`/resize/etc. are safe no-ops (Capabilities all false except `ExternalClient`). Credentials: do **not** pass passwords on the command line; let mstsc prompt (documented limitation, log Info once).
- [ ] **Step 4: Wire engine routing + UI:** in `MainWindow.ConnectAsync` after successful fallback connect, show the SessionWindow-style surface in "external client" state (embedded surface shows text "Session opened in Windows Remote Desktop" with a Back button) since HostControl is null — replace the current dead-end `EmbeddedStatus` text path.
- [ ] **Step 5: Tests + build green; commit** `feat: fall back to mstsc.exe when the embedded RDP control is unavailable`

### Task 4: Live cancel + connect telemetry

**Files:**
- Modify: `src/VMDesk.App/Views/MainWindow.xaml.cs:158-171`
- Modify: `src/VMDesk.App/Views/ConnectionProgressWindow.xaml.cs`
- Modify: `src/VMDesk.Application/Services/ConnectionOrchestrator.cs:111-121`

- [ ] **Step 1:** In `MainWindow.ConnectAsync`, create `using var cts = new CancellationTokenSource();`, pass `cts.Token` to `_sessions.ConnectAsync(..., cts.Token)` (parameter already exists), and set `progressDialog.CancelRequested += (_, _) => cts.Cancel();`. Log each cancel via `_log.Info`.
- [ ] **Step 2:** In `ConnectionProgressWindow`, keep double-fire guard (already disables button).
- [ ] **Step 3:** `TryCancelPendingConnect` bare catch → `_log.Debug("Best-effort cancel after failed attempt: " + ex.Message)`. Add an Info log line in `MicrosoftRdpSession` when `_connectTcs` resolves by cancel vs result so field hangs are diagnosable.
- [ ] **Step 4:** Build + existing orchestrator tests green (cancel paths already covered in `ConnectionOrchestratorTests.cs`). Commit `fix: wire the connect-progress Cancel button to a real CancellationToken`

## Phase B — VM discovery

### Task 5: Discovery model + catalog sync

**Files:**
- Modify: `src/VMDesk.Core/Entities/Entities.cs` (add to `VirtualMachineEntity`: `public string Provider { get; set; } = "Manual";`, `public string ProviderId { get; set; } = string.Empty;`, `public string PowerState { get; set; } = string.Empty;`)
- Modify: `src/VMDesk.Infrastructure/Persistence/VmDeskDbContext.cs` + new EF migration (or `EnsureCreated` path handles it — check `__EFMigrationsHistory` is empty in user DB → schema is EnsureCreated-built; then simply re-running `EnsureCreatedAsync` does **not** add columns. Add the columns with guarded `ALTER TABLE … ADD COLUMN` raw SQL in `DatabaseBootstrapper.BootstrapAsync` after migration, idempotent via `PRAGMA table_info(VirtualMachines)`).
- Create: `src/VMDesk.Application/Services/DiscoverySyncService.cs`
- Test: `tests/VMDesk.Application.Tests/DiscoverySyncTests.cs`

**Interfaces:**
- Produces: `record DiscoveredVm(string Provider, string ProviderId, string Name, string? Host, string PowerState)`; `DiscoverySyncService.SyncAsync(IReadOnlyList<DiscoveredVm> found): Task<SyncReport(int Added, int Updated, int StaleMarked)>` — matches existing rows on (Provider, ProviderId); manual rows untouched; discovered rows whose provider vanished keep the entry but set `PowerState = "Unknown"`.

- [ ] **Step 1: Failing tests** (in-memory fake repo already patterned in `TestDoubles.cs`): add-then-update-then-stale cycle; manual VM with same host not touched.
- [ ] **Step 2–4: Implement sync service, entity columns + idempotent ALTER, commit** `feat: model discovered VMs in the catalog with provider sync`

### Task 6: Discovery providers (Hyper-V, VirtualBox, VMware) + aggregation

**Files:**
- Create: `src/VMDesk.Core/Interfaces/IVmDiscoveryProvider.cs` (`string ProviderName { get; }`, `bool IsAvailable()`, `Task<IReadOnlyList<DiscoveredVm>> DiscoverAsync(CancellationToken ct)`)
- Create: `src/VMDesk.Infrastructure/Discovery/ProcessRunner.cs` (`interface IProcessRunner { Task<ProcessResult> RunAsync(string file, string args, CancellationToken ct); }` + impl; `record ProcessResult(int ExitCode, string StdOut, string StdErr)`)
- Create: `src/VMDesk.Infrastructure/Discovery/HyperVDiscoveryProvider.cs` — WMI `root\virtualization\v2` → `Msvm_ComputerSystem` where `Caption="Virtual Machine"`; on `ManagementException`/access-denied return empty + set `UnavailableReason` ("Hyper-V WMI needs elevation — run VMDesk as administrator to list Hyper-V VMs"). Use `System.Management` (in-box, no new package).
- Create: `src/VMDesk.Infrastructure/Discovery/VirtualBoxDiscoveryProvider.cs` — `VBoxManage.exe list vms` + `showvminfo <name> --machinereadable`; parse `name="…"`, `VM State="running|powered off"`, `Guest IP` from `guestproperty/get` best-effort.
- Create: `src/VMDesk.Infrastructure/Discovery/VMwareDiscoveryProvider.cs` — `vmrun.exe list` (Workstation path probe), similar best-effort.
- Create: `src/VMDesk.Application/Services/DiscoveryService.cs` — runs all available providers in parallel, aggregates, calls `DiscoverySyncService`, returns `DiscoveryRunReport(IReadOnlyList<ProviderStatus> Providers, SyncReport Sync)`; `record ProviderStatus(string Name, bool Available, int Found, string? Note)`; every failure is captured in `ProviderStatus.Note`, never thrown.
- Test: `tests/VMDesk.Infrastructure.Tests` does not exist → put parser tests in `tests/VMDesk.Application.Tests/DiscoveryParserTests.cs` by exposing parsers as `internal static` (InternalsVisibleTo already used? verify `VMDesk.Application.Tests` refs; else make parsers public static on the providers).

- [ ] **Step 1: Failing parser tests** with canned `VBoxManage` output:

```csharp
[Fact]
public void Parses_vbox_list_and_state()
{
    var vms = VirtualBoxDiscoveryProvider.ParseList("\"Win11-Test\" {b482f1d1-1111-2222-3333-444455556666}\n\"Ubuntu\" {aa...}");
    Assert.Equal(2, vms.Count);
    Assert.Equal("Win11-Test", vms[0].Name);
}
```

- [ ] **Step 2: Implement providers.** Hyper-V host for RDP: read `Msvm_KvpExchangeComponent` "com.microsoft.rdp.connect.host"/IP gathering best-effort; if none, `Host=null` → the UI offers Console mode (Task 7) instead of RDP. Providers must complete within 15s or be noted as timed out.
- [ ] **Step 3: Register in `App.OnStartup`** composition block (after catalog, ~line 110): construct providers, `DiscoveryService`; expose via property for MainWindow ctor param.
- [ ] **Step 4: Tests + build; commit** `feat: discover Hyper-V, VirtualBox and VMware VMs automatically`

### Task 7: Discovery UI surface + Hyper-V console connect

**Files:**
- Modify: `src/VMDesk.App/ViewModels/MainViewModel.cs` (expose `IsDiscovering`, `DiscoveryNote`, `DiscoverAsync()`)
- Modify: `src/VMDesk.App/Views/MainWindow.xaml` (sidebar "Discovered" entry + refresh glyph; note banner when providers unavailable/elevation needed)
- Modify: `src/VMDesk.Rdp`/`MstscExternalSession.cs` (generalize to `ExternalProcessSession` accepting an exe+args; Hyper-V VMs with `Provider=="HyperV"` connect via `vmconnect.exe <hostname> <vmid>`)
- Test: `tests/VMDesk.Application.Tests/DiscoveryUiStateTests.cs`

- [ ] **Step 1–2:** failing test: after `DiscoverAsync`, `Vms` contains provider rows and `DiscoveryNote` surfaces elevation hint when the Hyper-V provider reported it. Implement, verify tile renders `PowerState` chip (Running/Stopped) using existing `SuccessBrush`/`MutedTextBrush`.
- [ ] **Step 3:** Manual connect flow unchanged; discovered VMs with Host get RDP; Hyper-V without Host get console; discovered VMs without either show an actionable error dialog ("No RDP address found for this VM — enable Enhanced Session or set the host in Edit").
- [ ] **Step 4:** Full green + commit `feat: surface discovered VMs in the library with one-click refresh`

## Phase C — Keep awake & session lifetime

### Task 8: Sleep prevention + configurable keep-alive and idle time

**Files:**
- Create: `src/VMDesk.Infrastructure/Power/SleepPreventionToken.cs` (IDisposable, ref-counted)
- Modify: `src/VMDesk.Application/Services/RemoteSessionManager.cs` (acquire token while ≥1 session Connected; release on last close)
- Modify: `src/VMDesk.Core/Models/SettingsModels.cs` — add `int RdpKeepAliveIntervalSeconds { get; set; } = 60;` (`0` = control default), `int SessionIdleDisconnectMinutes { get; set; } = 0;` (`0` = keep forever while connected), `bool PreventSleepDuringSession { get; set; } = true;`
- Modify: `src/VMDesk.Core/Entities/Entities.cs` — per-VM overrides `int? KeepAliveIntervalSeconds`, `int? IdleDisconnectMinutes` (plus the same guarded ALTER pattern as Task 5)
- Modify: `src/VMDesk.Rdp/MicrosoftRdpSession.Options.cs:89` — `advanced.keepAliveInterval = (int)keepAliveSeconds * 1000` clamped to [0, …]; 0 → leave control default (skip the assignment)
- Modify: `src/VMDesk.App/Views/SettingsWindow.xaml` + AddVmWindow (advanced section) — expose the three settings
- Create: `src/VMDesk.Application/Services/IdleSessionWatchdog.cs` — `Start(session, minutes, onElapsed)`: DispatcherTimer-free, `System.Threading.Timer`; any `StateChanged` resets it; elapsed → `onElapsed` → UI closes session with a notification banner (not a modal)

- [ ] **Step 1: Failing test** for token ref-counting logic (pure):

```csharp
[Fact] public void Second_acquire_does_not_release_first()
{
    var (t1, t2) = (SleepPreventionToken.Acquire(NullLog), SleepPreventionToken.Acquire(NullLog));
    t1.Dispose(); Assert.False(SleepPreventionToken.IsActive);
    t2.Dispose(); Assert.True(SleepPreventionToken.IsActive == false);
}
```

(Expose `internal static bool IsActive` for tests; InternalsVisibleTo the Application tests assembly from Infrastructure, or host the counter in Application and only the P/Invoke in Infrastructure.)

- [ ] **Step 2: Implement** with ES_DISPLAY_REQUIRED combined only in Standalone/External mode:

```csharp
[DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(ES_CONTINUOUS | …) — store previous flags;
Acquire(): ES_CONTINUOUS | ES_SYSTEM_REQUIRED (| ES_DISPLAY_REQUIRED if externalSession);
```

- [ ] **Step 3:** Wire watchdog settings through `App.OnStartup` into `RemoteSessionManager`; add idle setting UI; test `IdleSessionWatchdogTests` (timer fires at injected clock; state change resets — inject `Func<TimeSpan>` + manual trigger to keep it deterministic).
- [ ] **Step 4:** Green + commit `feat: keep the system awake during live sessions with configurable keep-alive and idle timeout`

## Phase D — Windows 11 Fluent UI + multi-theme

### Task 9: Theme engine consolidation

**Files:**
- Delete: `src/VMDesk.App/Resources/Dark.xaml`, `Light.xaml`, `Teal.xaml`, `Ocean.xaml`, `Fluent.xaml`, `WindowsLight.xaml`, `WindowsDark.xaml` (all confirmed dead: no references outside obj/)
- Modify: `src/VMDesk.App/Resources/Themes/Light.xaml` + `Dark.xaml` — full Win11 token set (add: `CardBrush`, `CardHoverBrush`, `DividerBrush`, `SubtleHoverBrush`, `AcrylicTintBrush`, `AccentSecondaryBrush`, `FocusVisualBrush`, `NavSelectedBrush`, fonts `FontFamily UiFontFamily = "Segoe UI Variable Text, Segoe UI"` + sizes)
- Create: `src/VMDesk.App/Resources/Themes/Accents.xaml` — brush keys resolved at runtime; `src/VMDesk.App/Services/ThemeService.cs`: `ApplyTheme(AppThemeMode)`, `ApplyAccent(string? hex)` (null → system accent color read from `HKCU\...\Themes` `ColorSystemAccentColor`), rewrites only the tokens dictionary at `Resources.MergedDictionaries[0]` keeping Controls.xaml at [1]; raises `ThemeChanged`
- Modify: `src/VMDesk.App/App.xaml.cs` — delegate to `ThemeService`; fix index-swap bug (currently `MergedDictionaries[0] = …` shifts nothing if App.xaml order changes later — make ThemeService resolve by marker: give the tokens dictionary an `x:Class`-free tag via `Source` path check)
- Modify: `src/VMDesk.Core/Enums` ThemeMode stays System/Light/Dark; add `string AccentColor { get; set; } = ""` to `AppSettingsModel`
- Modify: sidebar theme buttons `MainWindow.xaml.cs:310-313` (ThemeClick) + SettingsWindow accent picker (6 swatches: Blue #0078D4, Teal #038387, Purple #8764B8, Mint #03875B, Orange #CA5010, Pink #E3008C)

- [ ] **Step 1:** Test (Application.Tests can't reference App; put the pure logic in `ThemeService` inside VMDesk.App and add `tests/VMDesk.App.Tests` xUnit project mirroring the existing test csproj pattern) — `ThemeServiceTests`: resolve Dark on `AppsUseLightTheme=0`, accent hex validation rejects malformed strings, merged-dictionary order stable after 10 toggles (headless-safe assertions via pure functions: extract `ThemeResolver.Resolve(mode, registryIsDark) → (isDark, dictionaryPath)` and test that).
- [ ] **Step 2:** Implement, delete dead files, full build.
- [ ] **Step 3:** Manual check: run app, toggle System/Light/Dark + each accent from sidebar and Settings; all windows restyle without restart.
- [ ] **Step 4:** Commit `feat: consolidate theming into light/dark/system with Windows 11 accent support`

### Task 10: Win11 window material — Mica, rounded corners, custom title bar

**Files:**
- Create: `src/VMDesk.App/Services/WindowMaterial.cs` — `EnableMica(Window, bool preferDark)`, `EnableRoundedCorners(Window)`, `SetTitleBarDarkMode(Window, bool dark)`
- Modify: `MainWindow`, `SessionWindow`, `AddVmWindow`, `SettingsWindow`, `CredentialManagerWindow`, `CredentialPickerWindow`, `ConnectionProgressWindow`, `DiagnosticsWindow`, `FastTransferWindow` code-behinds: call on `SourceInitialized`, re-apply on `ThemeChanged`
- Modify: `MainWindow.xaml` + others: `WindowStyle="SingleBorderWindow"` stays, but MainWindow gets `WindowChrome` custom title bar (caption buttons min/max/close as Fluent icon buttons, 46px height, drag + double-click maximize)

- [ ] **Step 1:** Implement P/Invokes:

```csharp
DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE=38, 2 /*Mica*/);   // fallback DWMWA_MICA_EFFECT=1021 (value 1) on build < 22H2
DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE=33, 2 /*Round*/);
DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE=20, preferDark ? 1 : 0);
```

- [ ] **Step 2:** Verify on this machine (build 26200 supports Mica): run app, confirm backdrop + dark title bar follow theme. Screenshot light + dark (user's verification loop expectation from memory: real captures, both themes).
- [ ] **Step 3:** Commit `feat: Windows 11 window material — Mica backdrop, rounded corners, themed title bar`

### Task 11: Fluent control restyle pass

**Files:**
- Modify: `src/VMDesk.App/Resources/Controls.xaml` (862 lines; the token-driven control templates)
- Modify: the 4 remaining hardcoded hexes found in Controls.xaml → tokens
- Modify: `MainWindow.xaml` — nav items become Win11 `NavigationViewItem` style (3px left selection pill `PrimaryBrush`, 7px radius hover), VM tiles become Fluent cards (radius 8, `CardBrush`, border `DividerBrush`, hover elevation via existing ShadowMedium, status chip pill), buttons: `Height=32`, radius 4, secondary variant = `CardBrush`+`BorderStrongBrush` border, primary = accent; typography: TitleLarge 28 SemiBold page titles, Body 14 for lists.

- [ ] **Step 1:** Restyle in one pass: Button, ToggleButton, ComboBox, TextBox, ListBox/ListViewItem, TreeViewItem, ScrollViewer (thin auto-hiding thumb), ProgressBar, ToolTip, MessageBox-like dialogs, Card, Badge, NavigationViewItem, title bar buttons.
- [ ] **Step 2:** Focus visuals: `Style="{x:Null}"`-free — every interactive control gets the Win11 2px black/white focus rect outside the bound (FocusVisualBrush).
- [ ] **Step 3:** Screenshot both themes, all five windows + embedded session surface; diff-check nothing references missing keys (the Sep-18 `ConnectionStateToVisibility` class of crash is caught here — run `scripts/verify-xaml.ps1`, which exists in the repo, and launch each window once).
- [ ] **Step 4:** Commit `feat: Windows 11 Fluent control styles across all windows`

### Task 12: Decompose MainWindow.xaml + final verification

**Files:**
- Modify: `src/VMDesk.App/Views/MainWindow.xaml` (673 lines) — extract `Controls/SidebarView.xaml`, `Controls/LibraryGridView.xaml`, `Controls/EmbeddedSessionView.xaml` UserControls with the same DataContext flow-through; behavior code moves to each control's code-behind or stays event-forwarded.
- Test: full suite + manual end-to-end.

- [ ] **Step 1:** Extract, build, verify XAML resource resolution by opening every window (incl. connect attempt with the progress dialog + cancel).
- [ ] **Step 2:** On this dev machine verify the full loop with a real connect: add a discovered/manual VM pointing at a reachable RDP host, connect (ActiveX absent here → mstsc fallback path must visibly engage and open), Cancel must abort within 1s, sleep token active during session (check `powercfg /requests` shows the process).
- [ ] **Step 3:** `dotnet test VMDesk.slnx -c Release` green; rebuild dist per repo convention (single-file standalone + installer scripts in `scripts/`); commit `build: rebuild dist executables` — this is the artifact the user installs.

---

## Risks / noted trade-offs

- **Hyper-V discovery needs elevation** for WMI on some configs — surfaced as an actionable banner, never a crash.
- **mstsc.exe fallback cannot pass saved credentials silently** (cmdline passwords are a security hole) — user types them once in mstsc; embedded ActiveX path keeps silent SSO when the control is available.
- **Mica on Windows 10 build < 22H2** falls back to solid `WindowBrush` — DWM calls are best-effort wrapped.
- `AxMSTSCLib` RCWs are committed binaries in `src/libs` — untouched; interop regeneration (`scripts/make-interop.ps1`) only if a coclass gap appears.

---

## Addendum (user request, 2026-09-22): jump-host nesting + Fluent icon pack

**Scenario to support:** the laptop can reach VM1 but NOT VM2; VM2 is only reachable *from* VM1. The user wants VM1 shown as a full-screen base view with the VM2 connection living inside that view.

**Ruling (no user answer available; chosen for "best experience" per their instruction):** implement BOTH halves — a real protocol hop so VM2 actually connects, and a base-canvas surface so it reads as nesting. A purely visual nesting cannot work: if the laptop cannot reach VM2, a direct RDP dial fails regardless of what the UI shows.

### Task 13: Jump-host broker (SSH tunnel via an intermediate VM)

**Files:**
- Create: `src/VMDesk.Core/Models/JumpHostModels.cs` — `record TunnelEndpoint(string LocalHost, int LocalPort)`; `interface IPortAllocator { int Allocate(); void Release(int port); }`
- Create: `src/VMDesk.Infrastructure/Networking/LoopbackPortAllocator.cs` — binds a `TcpListener` on `127.0.0.1:0`, returns the free port, holds/releases the reservation.
- Create: `src/VMDesk.Infrastructure/Networking/SshTunnelProcess.cs` — `interface ITunnelLauncher { Task<ITunnelHandle> StartAsync(SshTunnelRequest req, CancellationToken ct); }`; impl builds `ssh.exe` args via `ProcessStartInfo.ArgumentList`, args: `-N -o ExitOnForwardFailure=yes -o BatchMode=no -o StrictHostKeyChecking=accept-new -L 127.0.0.1:<localPort>:<targetHost>:<targetPort> <user>@<jumpHost>`; verify with a TCP connect probe to the local port (≤ 8 attempts × 500 ms) before reporting ready; `Dispose` kills the process; the real `ssh.exe` path comes from `%SystemRoot%\System32\OpenSSH\ssh.exe` with a PATH fallback.
- Modify: `src/VMDesk.Core/Entities/Entities.cs` — add `string JumpHostVmId { get; set; } = ""` (GUID of the intermediate VM, empty = direct) via the Task 5 idempotent-ALTER helper.
- Modify: `src/VMDesk.Application/Services/RemoteSessionManager.cs` — when a VM has `JumpHostVmId`, resolve that VM → dial/connect it first (its own session, or reuse a live one) → then start the tunnel through it → rewrite the dial target to `127.0.0.1:<allocatedPort>` before the RDP session connects; release the port and kill the tunnel when the nested session closes.
- Modify: `src/VMDesk.App/Views/AddVmWindow.xaml` — a "Connect through (jump host)" VM picker + "requires VM1 to be reachable" hint.
- Test: `tests/VMDesk.Infrastructure.Tests/` (project created in Task 6) — `SshTunnelArgsTests`: exact ArgumentList content, a jump-host username containing spaces cannot inject extra ssh switches, `ExitOnForwardFailure` present; `PortAllocatorTests`: distinct ports, release reuses.
- Test: `tests/VMDesk.Application.Tests/JumpHostFlowTests.cs` — with a fake `ITunnelLauncher` + fake engine: nested connect allocates a port, tunnel started against the jump host's host/user, RDP target rewritten to `127.0.0.1:<port>`, closing the nested session releases both; jump host not reachable → actionable error naming VM1, never a bare timeout.

**Known limitation to state in UI text:** `BatchMode`/key auth is attempted first; if VM1 demands a password, ssh prompts in a visible console window (create the process with a window so the prompt is reachable) — documented, not silently swallowed.

### Task 14: Base-VM canvas with nested sessions

**Files:**
- Create: `src/VMDesk.App/Views/BaseCanvasWindow.xaml` + `.cs` — full-screen (or "fill monitor") surface showing the base VM's session; a right rail lists VMs whose `JumpHostVmId` == this VM; activating one opens its session as a **nested child surface inside the canvas** (a `WindowsFormsHost` for the nested RDP control laid over the base surface with a draggable Fluent title bar: VM name, state chip, restore/maximize/close, "Send Ctrl+Alt+End"); minimizing the nested surface reveals the base VM beneath it.
- Create: `src/VMDesk.App/Services/NestedSessionPresenter.cs` — owns the z-order/geometry of nested hosts inside a canvas; guarantees a nested control is parented **before** its `StartConnect` (Task 2 invariant) and unparented + released on close.
- Modify: `src/VMDesk.App/ViewModels/MainViewModel.cs` — expose `HasJumpHostChildren(vm)`, and a "Open as base view" command.
- Modify: `src/VMDesk.App/Views/MainWindow.xaml` — tile context menu + toolbar entry for the base view; the embedded surface gains a "Fullscreen base view" toggle reusing `fullscreen.ico`.
- Constraint: two live RDP controls in one window is already supported by the control; each nested host gets its own `WindowsFormsHost`. Keep the base surface's `SmartSizing` on so the canvas fills the monitor.
- Manual verification (Task 12): base VM fullscreen, nested VM2 opens over it, close/restore/reconnect behave, no orphaned ssh processes (`tasklist` check).

### Task 15: Fluent icon pack integration

**Files:**
- Copy: `ico/*.ico` → `src/VMDesk.App/Resources/Icons/` (keep names; `git add` the pack).
- Modify: `src/VMDesk.App/VMDesk.App.csproj` — `<ApplicationIcon>Resources\Icons\app.ico</ApplicationIcon>` (the exe identity icon is currently MISSING — `Resources\app.ico` is referenced as a `None Update` item that does not exist, so no icon is ever embedded); add `<Resource Include="Resources\Icons\*.ico" />`.
- Create: `src/VMDesk.App/Controls/IconImage.cs` or a `MarkupExtension`/converter-free approach: `src/VMDesk.App/Resources/Icons.xaml` — an `ImageSource` resource per icon (`{BitmapImage UriSource="Icons/connect.ico"}` via pack URI) keyed `Icon.Connect`, `Icon.Vm`, `Icon.Settings`, … one per file.
- Modify: every `TextBlock FontFamily="Segoe MDL2 Assets"` at ≥16px → `Image Source="{StaticResource Icon.X}"` at the same box size; 10–12px status chips keep their glyphs (a colored 256px bitmap at 10px blurs; the pack's 16px layer is the floor).
- Modify: `App.xaml` — delete the hand-drawn `AppIcon` `DrawingImage` placeholder and point window `Icon` + tray icon at `Icon.App`; set `Icon` on all nine windows so the title bar and taskbar use the pack.
- Modify: `installer/VMDesk.iss` — `DefaultIconName`/`Source` icon entries to the pack; `scripts/publish.ps1` — confirm the single-file build embeds `app.ico`.
- Test: `tests/VMDesk.App.Tests/IconResourcesTests.cs` — every `.ico` in `Resources/Icons` has a matching `Icon.<Name>` key in `Icons.xaml`, and every `Icon.*` key referenced from any `.xaml` in the app resolves (guards against the `ConnectionStateToVisibility`-class StaticResource crash from the Sep-18 logs); the 22 MDL2 glyph sites are enumerated so a later task cannot silently add a 16px+ glyph back.

**Order of execution:** 5 → 6 → 7 → 8 → **13 → 14** → 9 → **15** → 10 → 11 → 12. Task 15 sits inside the UI phase so the restyle (Task 11) lands with final icons; Task 14's canvas is restyled by Task 11 too.
