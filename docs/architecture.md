# Architecture

VMDesk uses a dependency direction of Core -> Application -> Infrastructure/UI.
The RDP adapter implements `IRemoteSessionEngine` and exposes only
`IRemoteSession` to application and UI code. ActiveX controls are created on the
STA UI thread and hosted with `WindowsFormsHost`.

The application composition root is `VMDesk.App/App.xaml.cs`. Startup creates the
SQLite context factory, bootstraps the schema, then composes logging, credentials,
catalog, diagnostics, import/export, backup, and session services.

SQLite lives in `%LOCALAPPDATA%\VMDesk\vmdesk.db`; credentials are keyed by a
deterministic VM reference in Windows Credential Manager.