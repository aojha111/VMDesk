# Reliability tasks

## Completed
- [x] Refresh Connect, Favorite and Delete enabled states when library loading starts/finishes.
- [x] Preserve saved credentials when removing VM metadata, including when repository deletion fails.
- [x] Add four isolated library regression cases.
- [x] Delete confirmation honors persisted ConfirmBeforeDelete; dialog defaults to No and explains metadata-only removal.
- [x] Delete command prevents duplicate execution and reports settings/deletion/refresh errors to the window.
- [x] Add six removal regression cases: confirmation accepted/declined/disabled, missing handler, failure/retry/re-entry, settings failure. All 15 tests pass.
- [x] Release build: zero warnings, zero errors.
- [x] Rebuilt app startup verified via Windows UI Automation (empty library, 0 VMs).

## Deferred — not implemented in this pass
- [ ] Async command re-entry protection and visible error feedback for remaining actions.
- [ ] Live Delete → No dialog verification (no saved VMs available in smoke-test app).
- [ ] Serialized/coalesced refresh and search handling.
- [ ] Saved-credential selection and cancellation behavior for Connect.
- [ ] Connection failure propagation, failed-session cleanup and retry tests.
- [ ] Native RDP lifecycle and real remote-machine end-to-end verification.
- [ ] Installer/portable package refresh after live validation.

## Verification
Tests: `dotnet test C:\MyProjects\VM-View\tests\VMDesk.Application.Tests\VMDesk.Application.Tests.csproj --configuration Release --no-restore --nologo --tl:off`
Build: `dotnet build C:\MyProjects\VM-View\src\VMDesk.App\VMDesk.App.csproj --configuration Release --no-restore --nologo --tl:off --output "C:\Users\Abhijit Ojha\AppData\Local\Temp\VMDesk-Reliability-Check"`

The tests use synthetic entities and interface substitutes, not saved data or credentials. They verify view-model notifications, not an actual remote connection. The separate build output avoids replacing files used by the already-running app.
