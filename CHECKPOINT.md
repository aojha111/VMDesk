# VMDesk checkpoint (created 2026-09-17)

## Current state

- Repo root: `c:\MyProjects\VM-View`
- SDK in use: dotnet CLI available; runtime target is Windows x64
- Builds verified locally:
  - `VMDesk.Core`
  - `VMDesk.Infrastructure`
  - `VMDesk.Application`
  - `VMDesk.Rdp`
- These four projects currently compile.
- `VMDesk.App` now compiles in Release and produces a runnable Windows x64 executable.
- The app boots the SQLite database, file logging, credential store, repository, and VM catalog.
- The main WPF library supports search, add VM, save credential, favorite, delete, and connect actions.
- The existing Microsoft RDP engine is wired through `RemoteSessionManager` into a WPF session window.
- Deterministic application tests exist under `tests/VMDesk.Application.Tests` and currently pass.
- `scripts/publish.ps1` produces a self-contained x64 publish directory and portable ZIP.
- `installer/VMDesk.iss` is provided for optional Inno Setup packaging.
- Build logs exist in root:
  - `build-app.log`
  - `build-infra.log`
  - `build-rdp.log`
  - `build-rdp2.log`

## What appears implemented in source

Core layer:
- Entity models, enums, interfaces, settings models
- VM entity, group/tags, connection settings, display options
- Core service contracts including sessions, repository, settings, credential store, import/export, diagnostics, logging

Infrastructure layer:
- SQLite persistence stubs/services
- Windows Credential Manager backing
- File log factory
- Diagnostics service
- App paths / configuration helpers

Application layer:
- VmCatalogService
- VmFilter
- RemoteSessionManager
- ConnectionOrchestrator
- ConnectionErrors
- WorkspaceLayoutService
- ImportExportService
- BackupRestoreService
- ConnectionTester
- AppVersion / no-update service

RDP layer:
- Microsoft RDP typelib interop
- AxMsRdpClient host wrapper / factory helpers
- MicrosoftRdpSession
- RDP option mapping
- RDP events wiring
- RDP commands for display, redirection, SAS, fullscreen handling
- File transfer service using redirected drives and clipboard file list

## Likely still missing or only partially present

- Full session workspace tabs/tiling and standalone native window mode
- WPF Win32/Fluent polish beyond the initial library/session shell
- Native container window for standalone RDP windows
- Tabbed/tiled workspace UI
- VM tile/list views
- Settings UI
- Import/export UI
- Diagnostics UI
- Log/viewer or diagnostics surface
- Full installer execution validation on a machine with Inno Setup installed
- Final runtime validation of:
  - real embedded RDP control integration
  - real standalone RDP window creation
  - credential read/write
  - SQLite create/read/update/delete
  - import/export round trip
  - backup/restore round trip
  - disconnect, reconnect, close, multiple sessions
  - resize and fullscreen handling
  - file transfer flow

## Build/packaging notes

- Targeted platform is Windows x64
- Need self-contained publish output path for portable exe
- Need installer output path
- Need to decide whether installer uses Inno Setup, WiX, or dotnet publishing plus a separate installer step

## What the next agent should do first

1. Confirm the four-project build still succeeds from a clean local build.
2. Find or create the application entry point and main window.
3. Decide windowing platform now if not already decided:
   - WPF is the most natural fit for Fluent-styled desktop shell plus Win32 hosting of an Ax control
4. Implement or locate:
   - Main app shell
   - VM library view
   - Session host window(s)
   - Embedded RDP control hosting in WPF
   - Standalone RDP window creation from the same session
5. Wire startup/startup services:
   - logging
   - database creation/opening
   - credential store availability check
   - settings load
6. Add publish step and installer step.
7. Add tests.
8. Run real manual validation of the major flows.

## Known risks from earlier work

- RDP interop depends on the Microsoft RDP ActiveX control and its generated wrapper.
- Event wiring and COM cleanup must be handled carefully.
- File copy/paste and drive redirection depend on RDP stack support and client configuration, not on app-side invention.
- If a feature is limited by the RDP control, implement the best supported behavior and document the limitation instead of faking it.

## Reading priority for planner

- `Master.MD`
- `layout.MD`

These are treated as the authoritative specification for VMDesk. Later statements in those files should be treated as authoritative if there is any conflict.

## Immediate goal

Produce a runnable Windows x64 application exe and a production installer, with the major flows working and the limitations documented.

## Validation completed 2026-09-17

- `dotnet build src/VMDesk.App/VMDesk.App.csproj --configuration Release` passes.
- `dotnet test tests/VMDesk.Application.Tests/VMDesk.Application.Tests.csproj --configuration Release` passes: 3 tests.
- `scripts/publish.ps1` passes and produces the complete release set under `dist/` (moved from `artifacts/` on 2026-09-19).
- Live RDP, ActiveX registration, credential manager behavior, and installer execution remain manual Windows validation items.
- Startup recovery was verified against the previously crashing partial database: EF bookkeeping-only files are repaired safely, and the VMDesk tables are recreated without deleting actual user tables.
- `VMDesk.slnx` now includes all production projects and the test project.
- Tile/List persistence, DataGrid list mode, import/export, backup, diagnostics, README, architecture/security/development/troubleshooting docs, artifacts, and SHA256 output are now present.
- Ocean Blue, Light, Dark, and Teal themes, functional/collapsible navigation, reusable credential selection, and a credential manager page are now present.
- Native clipboard/file transfer actions are exposed in the session toolbar; file copies use asynchronous sequential 4 MiB buffers and RDP redirection rather than a simulated transport.
- `dist/VMDesk-Setup-x64.exe` is generated with Windows IExpress when Inno Setup is unavailable.
- The release executables are committed under `dist/` (`VMDesk.exe`,
  `VMDesk-Setup-x64.exe`, `SHA256SUMS.txt`), so a runnable build ships with the
  repository without requiring local tooling.
- The shell was redesigned with guide-aligned enterprise styling: icon menus, collapsible navigation, Settings-owned theme selection, and a refreshed self-contained payload.
- Credential Manager now owns add/update/delete; Add VM selects a saved credential by name instead of accepting a password.
- VM launch mode now supports embedded workspace hosting or a separate session window.
- The hero banner and sidebar can collapse independently, and fast transfer uses redirected drives with async sequential 4 MiB I/O plus native clipboard file lists.

## Validation completed 2026-09-19

- Connect flow repaired: the connect progress window is non-modal and closes
  itself; connection failures propagate to the UI and are recorded on the VM
  tile instead of being swallowed; dead sessions are replaced on retry instead
  of being reused; timeouts are reported as timeouts rather than
  "Connection cancelled"; the connect guard prevents double-connects.
- Credential selection stays in the Credential Manager page for maintenance;
  connecting a VM without a usable credential opens `CredentialPickerWindow`,
  a dropdown of saved credentials, and remembers the choice on the VM.
- Launch mode resolution (`SessionLaunchResolver`) routes sessions to the
  embedded workspace or an independent standalone window per VM.
- Unit tests added: SessionLaunchResolverTests, VmLaunchModePersistenceTests,
  WorkspaceSessionManagerTests, VmConnectGuardTests, ConnectionOrchestratorTests,
  and SessionCredentialGuardTests. Full suite: 53 tests passing (46 Application
  + 7 Rdp) with zero warnings.
- Release output moved from `artifacts/` to `dist/`; publish.ps1,
  build-installer.ps1, and the setup bootstrap tool now write to `dist/`, and
  the committed executables live there (`VMDesk.exe`, `VMDesk-Setup-x64.exe`,
  `SHA256SUMS.txt`).
- Live RDP validation against a real host, credential manager behavior, and
  installer execution remain manual Windows validation items.
