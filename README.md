# VMDesk

VMDesk is an offline-first Windows x64 VM and Remote Desktop connection manager.
It stores VM metadata in SQLite, stores passwords only in Windows Credential Manager,
and hosts Microsoft's native RDP ActiveX control for connected sessions.

## Features

- Tile and persisted list views with search, favorites, CRUD, and credentials.
- Native Microsoft RDP sessions through `mstscax.dll`.
- Import/export without passwords, timestamped database backups, diagnostics, and logs.
- Self-contained Windows x64 publishing and optional Inno Setup packaging.

## Requirements

Development requires the .NET 10 SDK and Windows 10/11 x64. Live RDP requires the
Microsoft Remote Desktop ActiveX control registered by Windows. Inno Setup 6 is
optional for building the installer.

## Build and run

```powershell
dotnet restore .\src\VMDesk.App\VMDesk.App.csproj
dotnet build .\src\VMDesk.App\VMDesk.App.csproj --configuration Release
dotnet test .\tests\VMDesk.Application.Tests\VMDesk.Application.Tests.csproj --configuration Release
dotnet run --project .\src\VMDesk.App\VMDesk.App.csproj --configuration Release
```

Publish with `scripts\publish.ps1`. It creates a self-contained folder and
`publish_output\VMDesk-win-x64.zip`. Run `scripts\build-installer.ps1` to create
`artifacts\VMDesk-Setup-x64.exe`; it uses Inno Setup when available and falls back
to the Windows IExpress/makecab toolchain otherwise.

## Data and security

User data is stored under `%LOCALAPPDATA%\VMDesk`, including `vmdesk.db`, backups,
exports, and rotating logs. Passwords are never written to SQLite, exports, or logs.
Normal uninstall should preserve this directory.

## Architecture

`VMDesk.Core` defines contracts and entities; `VMDesk.Application` owns workflows;
`VMDesk.Infrastructure` implements SQLite, credentials, logging, and diagnostics;
`VMDesk.Rdp` isolates ActiveX/COM; `VMDesk.App` contains the WPF shell and views.

See [docs/architecture.md](docs/architecture.md), [docs/security.md](docs/security.md),
[docs/development.md](docs/development.md), and [docs/troubleshooting.md](docs/troubleshooting.md).