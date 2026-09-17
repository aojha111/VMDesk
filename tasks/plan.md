# VMDesk reliability improvement plan

## Scope
Fix confirmed command, credential-safety and connection-error defects without changing the database schema or adding dependencies. Preserve the existing Connect notification change.

## Ordered tasks
1. Reliable library commands: introduce a tested async command with re-entry prevention and error reporting; notify all busy-dependent commands; serialize refresh requests. Acceptance: errors are observable, commands re-enable, refresh requests are not discarded.
2. Safe removal: confirm before removal according to settings; never delete shared credentials with VM metadata. Acceptance: cancellation leaves data unchanged and credential deletion is never called by catalog removal.
3. Predictable Connect: use existing saved credentials, respect picker cancellation, prevent repeated launch requests, propagate failed connection attempts and clean up failed sessions. Acceptance: failure reaches the caller and subsequent retry creates a new session.
4. Verification: focused regression tests, full existing test suite, Release build and local UI smoke test. Live authentication requires a user-selected remote VM and is not claimed by local tests.

## Architecture
Keep WPF/WinForms and existing xUnit/.NET 10 projects. Link UI-independent view-model command sources into application tests rather than adding a UI framework dependency. Do not alter ActiveX initialization without a reproducible failure.

## Risks and follow-up
- Real RDP hosting, authentication, disconnect/reconnect and window ownership still require end-to-end validation.
- Installed/portable binaries are separate from build output; packaging is not part of this pass.
- Do not log passwords or inspect credential values during verification.
