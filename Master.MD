

You are a senior Windows desktop architect and principal .NET engineer.

Build a complete, production-quality Windows desktop application from scratch.

APPLICATION NAME:
"VMDesk"

PURPOSE:
Create a standalone Windows application for managing saved Virtual Machine / Remote Desktop connections and connecting to them through Microsoft Remote Desktop technology.

The application should feel like the classic Microsoft Remote Desktop client, but with a significantly better VM library, tile/list management, favorites, tagging, session organization, saved credentials, and multi-session workspace.

IMPORTANT:
Do not merely create a prototype, mockup, skeleton, demo, or partially implemented UI.

Build the actual working application.

Do not ask me unnecessary clarification questions.
Make sensible engineering decisions using the requirements below.
When two approaches are possible, choose the approach that is most native, reliable, maintainable, secure, and Windows-friendly.



# VMDesk — Master Specification

## Document Authority

This document is the authoritative functional and technical specification
for the VMDesk application.

All requirements in this document are mandatory unless explicitly marked
as optional, future scope, or unsupported by the underlying Windows/RDP API.

Later sections override earlier sections when there is a direct conflict.

The application must be implemented as a real production-quality Windows
desktop application, not as a prototype or mockup.

The coding agent must inspect the complete document before implementation.

============================================================

1. TECHNOLOGY STACK
   ============================================================

Target:

* Windows 10/11 x64
* Windows desktop only

Primary framework:

* .NET 10 LTS if available in the development environment.
* If .NET 10 is unavailable, use the newest installed supported LTS version.
* WPF
* C#

Architecture:

* MVVM
* Dependency Injection
* Clean Architecture / modular architecture
* Async/await wherever appropriate
* Nullable reference types enabled
* Implicit usings enabled
* Treat compiler warnings as errors where practical

UI:

* WPF
* Modern Fluent-inspired Windows UI
* Dark mode
* Light mode
* Follow Windows system theme option
* DPI aware
* High-DPI compatible
* Responsive layouts
* Keyboard friendly
* Accessible controls
* Modern icons
* Smooth animations only where useful

Database:

* SQLite
* Entity Framework Core
* Local database
* Database schema versioning/migrations

Remote Desktop:
USE MICROSOFT'S NATIVE WINDOWS REMOTE DESKTOP ACTIVEX CONTROL / MSTSCAX IMPLEMENTATION.

Do NOT implement an RDP protocol from scratch.

Use the native Microsoft Remote Desktop ActiveX interfaces available on Windows.

WPF should host the required Windows/ActiveX control using the appropriate WindowsFormsHost/interop layer.

The RDP layer must be isolated behind an interface so that another RDP engine could theoretically be added later.

Example:

IRemoteSessionEngine
ConnectAsync(...)
DisconnectAsync(...)
ReconnectAsync(...)
SendCtrlAltDel(...)
ToggleFullscreen(...)
Resize(...)
SetSmartSizing(...)
SetClipboardRedirection(...)
SetAudioRedirection(...)
SetDriveRedirection(...)
SetPrinterRedirection(...)
SetKeyboardHook(...)
etc.

Primary implementation:

MicrosoftRdpSessionEngine

============================================================
2. HIGH LEVEL PRODUCT
=====================

The application has two major areas:

A. VM LIBRARY
B. REMOTE SESSION WORKSPACE

The user should be able to:

* Add VM
* Edit VM
* Delete VM
* Duplicate VM
* Favorite VM
* Search VM
* Filter VM
* Tag VM
* Group VM
* Sort VM
* Connect VM
* Disconnect VM
* Reconnect VM
* Open VM in a new tab
* Open multiple VMs simultaneously
* Tile active sessions
* Switch between sessions
* Maximize a session
* Full-screen a session
* Save credentials
* Forget credentials
* Test connection
* Import/export configuration
* Backup database
* Restore database

============================================================
3. MAIN WINDOW
==============

Create a modern main window similar in concept to a modern Remote Desktop manager.

Layout:

LEFT SIDEBAR

Sections:

* All VMs
* Favorites
* Recent
* Connected
* Disconnected
* Groups
* Tags
* Settings

BOTTOM SIDEBAR:

* Theme
* Application Settings
* About

TOP TOOLBAR:

* Search
* Add VM
* Quick Connect
* Refresh
* View selector
* Tile sessions
* Fullscreen
* Settings

MAIN CONTENT AREA:

Support two modes:

1. TILE VIEW
2. LIST VIEW

Persist the selected mode.

============================================================
4. TILE VIEW
============

Create beautiful VM cards.

Each VM tile should show:

* VM icon
* VM display name
* Host/IP
* Port
* Username
* Operating system
* Group
* Tags
* Favorite indicator
* Connection status
* Last connected time
* Last connection result
* Optional latency
* Connect button
* More menu

Connection status:

* Connected
* Connecting
* Disconnected
* Error
* Unknown

Color/status indicators must not be the only way of communicating state.

Cards should support:

* single click
* double click
* right click
* drag/drop
* favorite
* edit
* duplicate
* delete
* connect

VM card context menu:

Connect
Reconnect
Disconnect
Edit
Duplicate
Favorite
Open Session
Test Connection
Copy Host
Copy Username
Export
Delete

============================================================
5. LIST VIEW
============

Create a professional DataGrid/List view.

Columns:

* Name
* Host
* Port
* Username
* Group
* Tags
* Status
* Last Connected
* Favorite

Allow:

* sorting
* filtering
* column resizing
* column visibility
* double-click connect
* context menu
* multi-select

Persist column configuration locally.

============================================================
6. VM MODEL
===========

Create a VM entity similar to:

VirtualMachine

Properties:

Id
Name
Description
Host
Port
Protocol
Username
CredentialReference
Domain
OperatingSystem
GroupId
Tags
Notes
Favorite
Color
Icon
CreatedAt
UpdatedAt
LastConnectedAt
LastConnectionStatus
LastConnectionError
LastKnownLatency
ConnectionProfileId

Additional connection settings:

ScreenWidth
ScreenHeight
ColorDepth
SmartSizing
FullScreen
MultiMonitor
UseAllMonitors
ClipboardRedirection
AudioRedirection
DriveRedirection
PrinterRedirection
SmartCardRedirection
PortRedirection
MicrophoneRedirection
KeyboardHookMode
ConnectionBarEnabled
PerformancePreset
ReconnectAutomatically

Use sane defaults.

Default protocol:

RDP

Design the protocol model so SSH/VNC/etc. can be added later.

============================================================
7. CREDENTIAL STORAGE
=====================

SECURITY IS CRITICAL.

NEVER store passwords in:

* SQLite plaintext
* JSON
* XML
* application configuration
* logs
* crash dumps
* source code

Do not implement your own weak encryption scheme.

Use the Windows Credential Manager / Windows credential APIs for saved credentials.

Store only a credential reference/key in SQLite.

Example conceptual model:

VM:
Username = "administrator"
CredentialReference = "VMDesk/{VM-GUID}"

Windows Credential Manager:
Target = VMDesk/{VM-GUID}
Username = administrator
Password = encrypted by Windows credential infrastructure

Implement:

ICredentialStore

Methods:

SaveCredentialAsync
GetCredentialAsync
DeleteCredentialAsync
CredentialExistsAsync
ClearAllCredentialsAsync

Credential operations must not expose plaintext passwords unnecessarily.

Do not write credentials to logs.

If the user selects:

"Do not save password"

the password must exist only for the active connection/session.

============================================================
8. ADD VM WINDOW
================

Create a professional Add/Edit VM dialog.

Sections:

GENERAL

Name
Description
Host/IP
Port
Protocol

AUTHENTICATION

Username
Domain
Password
Remember password checkbox

DISPLAY

Resolution
Smart sizing
Color depth
Fullscreen
Multi-monitor
Use all monitors

REDIRECTION

Clipboard
Audio
Microphone
Drives
Printers
Smart cards
Ports

PERFORMANCE

Preset:

* Automatic
* LAN
* Broadband
* High latency
* Low bandwidth

ADVANCED

Keyboard hook
Connection bar
Reconnect automatically
Admin session
Gateway settings
Gateway username
Gateway credentials

Buttons:

Test Connection
Save
Cancel

Password fields must never display plaintext unless the user explicitly chooses to reveal it.

============================================================
9. RDP ENGINE
=============

Create a dedicated RDP integration layer.

Example:

/RemoteDesktop
IRdpSession.cs
IRdpSessionFactory.cs
MicrosoftRdpSession.cs
MicrosoftRdpSessionFactory.cs
RdpConnectionOptions.cs
RdpSessionState.cs
RdpSessionEvents.cs
RdpInterop/
ActiveXInterop.cs
MsRdpClientInterop.cs
NativeMethods.cs

Use the Windows Remote Desktop ActiveX control through COM/interop.

The engine must support:

* hostname/IP
* port
* username
* domain
* password
* NLA
* connection
* disconnection
* reconnect
* session events
* connection status
* connection errors
* connection state
* display resize
* smart sizing
* fullscreen
* clipboard redirection
* audio redirection
* drive redirection
* printer redirection
* smart card redirection
* keyboard hook
* multi-monitor where supported
* admin session where supported
* connection bar
* performance options
* RDP gateway where supported

Do not expose COM implementation details to the ViewModels.

Wrap COM interop behind interfaces.

============================================================
10. COPY / PASTE / CLIPBOARD
============================

Clipboard support is a core feature.

Enable RDP clipboard redirection by default.

Support:

LOCAL -> REMOTE

Ctrl+C
Ctrl+V

REMOTE -> LOCAL

Ctrl+C
Ctrl+V

Support text and normal clipboard formats supported by the RDP channel.

Do not implement dangerous clipboard interception.

Do not log clipboard content.

Provide per-VM:

Clipboard enabled/disabled

Also provide a session toolbar toggle.

If clipboard redirection is disabled:

* Local clipboard remains local.
* Remote clipboard remains remote.

============================================================
11. SMART RESIZE
================

Implement three display modes:

1. Smart Fit
2. Native Resolution
3. Fullscreen

Smart Fit should scale the remote desktop to the available session area.

When the application window is resized:

* remote session should behave intelligently
* avoid unnecessary reconnects
* maintain good visual quality
* preserve aspect ratio when appropriate

Expose SmartSizing using the native RDP functionality where supported.

Do not fake resize behavior using screenshots.

============================================================
12. MULTI-MONITOR
=================

Support:

* single monitor
* selected monitor
* all monitors where supported by the native RDP control/configuration

Create display options:

Monitor:
[Primary]
[Current]
[All]

Full screen:
[Enabled]

Smart sizing:
[Enabled]

For unsupported combinations, show a clear user-facing message and fall back gracefully.

============================================================
13. MULTI-SESSION WORKSPACE
===========================

This is a core feature.

The user should be able to connect to multiple VMs simultaneously.

Example:

VM A
VM B
VM C
VM D

Each connection should have its own RDP session object.

The workspace must support:

TAB MODE

[VM-A] [VM-B] [VM-C]

and

TILE MODE

+----------------+----------------+
|                |                |
|     VM-A       |      VM-B      |
|                |                |
+----------------+----------------+
|                |                |
|     VM-C       |      VM-D      |
|                |                |
+----------------+----------------+

Supported layouts:

1 x 1
1 x 2
2 x 1
2 x 2
3 x 2
2 x 3
Auto

Automatically choose a good layout based on the number of sessions.

Each session can be:

* maximized
* restored
* detached into a new window if practical
* closed
* reconnected

Provide:

"Tile All"

"Stack All"

"Maximize Current"

"Close All"

"Reconnect All"

"Disconnect All"

============================================================
14. SESSION TOOLBAR
===================

When a remote session is active, display a small unobtrusive toolbar.

Actions:

Disconnect
Reconnect
Fullscreen
Smart Fit
Native Size
Clipboard
Audio
Drives
Send Ctrl+Alt+Del
Tile Sessions
Session Info
Connection Settings

Toolbar can auto-hide.

Keyboard shortcut:

Ctrl+Shift+F
Toggle fullscreen

Ctrl+Shift+S
Smart sizing

Ctrl+Shift+T
Tile sessions

Ctrl+Shift+R
Reconnect

Ctrl+Shift+D
Disconnect

Do not override normal Ctrl+C / Ctrl+V behavior inside the remote session.

============================================================
15. SEND CTRL+ALT+DEL
=====================

Provide a "Send Ctrl+Alt+Del" action using the supported RDP mechanism.

Do not synthesize OS-level Ctrl+Alt+Del using unsafe global keyboard injection.

============================================================
16. CONNECTION STATUS
=====================

Create a central state model.

States:

Disconnected
Connecting
Connected
Reconnecting
Disconnecting
Failed

Display:

Connected
Connecting...
Reconnecting...
Disconnected
Connection failed

Do not freeze the UI during network operations.

All long-running operations must be asynchronous where the API allows it.

============================================================
17. CONNECTION ERROR HANDLING
=============================

Create user-friendly error messages.

Examples:

Unable to resolve host.

Connection refused.

Network unavailable.

Authentication failed.

Credentials rejected.

NLA failure.

RDP gateway unavailable.

Connection timeout.

Remote computer disconnected.

Unsupported configuration.

Do not show raw COM exception dumps to ordinary users.

Provide:

"Show technical details"

for diagnostics.

============================================================
18. QUICK CONNECT
=================

Add a Quick Connect function.

User enters:

hostname/IP
port
username
password

Then:

Connect

Option:

Save VM after successful connection.

Example:

Host:
192.168.1.50

Port:
3389

Username:
administrator

[Connect]

============================================================
19. SEARCH
==========

Global search should search:

VM name
host
IP
username
description
group
tags
notes

Search must be fast.

Use debounced search.

Support keyboard navigation.

============================================================
20. GROUPS AND TAGS
===================

Groups:

Production
Development
Testing
Local
Cloud
Database
Web
Other

But allow completely custom groups.

Tags:

Windows
Linux
AWS
Azure
VMware
Hyper-V
Database
Docker
Development

Again, user-defined.

VMs can have multiple tags.

============================================================
21. FAVORITES
=============

Favorite VMs appear at the top.

Provide:

Favorite filter
Favorite sorting
Favorite keyboard shortcut

============================================================
22. RECENT CONNECTIONS
======================

Store:

LastConnectedAt

Show recent VMs.

Provide:

Recent connections list.

Allow user to clear recent history.

Do not store passwords in recent history.

============================================================
23. CONNECTION TEST
===================

"Test Connection"

should validate:

* DNS/hostname resolution
* TCP reachability to configured RDP port
* credential/session connection if the user explicitly requests full authentication test

Clearly distinguish:

Network reachable

from:

Authentication successful

Do not claim a complete RDP connection is successful if only TCP connectivity was tested.

============================================================
24. VM STATUS
=============

Provide optional lightweight status checks.

Status:

Online
Offline
Unknown
Connected

Do not continuously scan the network.

Allow:

Refresh status

and optionally configurable periodic polling.

Polling default:

Disabled.

============================================================
25. IMPORT / EXPORT
===================

Create an export/import system.

Export VM metadata to:

.json

IMPORTANT:

Never export passwords in plaintext.

Credentials should have a separate explicit secure export flow.

Normal export contains:

VM configuration
groups
tags
display settings
connection settings

No passwords.

When importing:

* detect duplicate VM IDs
* offer Merge / Create New / Skip
* validate the schema
* maintain backward compatibility

============================================================
26. BACKUP / RESTORE
====================

Provide:

Backup Database

Restore Database

Backup should create a timestamped file.

Before restore:

* validate database
* create automatic backup of current database

Never silently overwrite the user's existing database.

============================================================
27. LOCAL DATABASE
==================

Use SQLite.

Suggested entities:

VirtualMachine
Group
Tag
VmTag
ConnectionProfile
ApplicationSetting
RecentConnection

Use EF Core migrations.

Database location:

%LOCALAPPDATA%\VMDesk\

Example:

%LOCALAPPDATA%\VMDesk\vmdesk.db

Do NOT store the database beside the executable.

============================================================
28. APPLICATION SETTINGS
========================

Settings:

Theme:
System
Light
Dark

Default VM view:
Tiles
List

Start with Windows:
On/Off

Minimize to tray:
On/Off

Reconnect sessions:
On/Off

Confirm before delete:
On/Off

Default RDP settings

Default clipboard behavior

Default display behavior

Database location information

Diagnostics/logging level

============================================================
29. WINDOWS TRAY
================

Support system tray optionally.

Tray menu:

Open VMDesk
Connect to recent VM
Connected sessions
Disconnect all
Exit

Do not keep sensitive credentials in tray state.

============================================================
30. LOGGING
===========

Use structured application logging.

Log:

Application startup
Shutdown
VM CRUD operations
Connection attempt
Connection success
Connection failure
Connection termination
Configuration errors

NEVER log:

Passwords
Clipboard contents
Tokens
Credential values

Logs:

%LOCALAPPDATA%\VMDesk\Logs\

Implement log rotation.

============================================================
31. SECURITY
============

Security requirements:

* no plaintext password storage
* Windows Credential Manager for credentials
* no credentials in logs
* no password values in exception messages
* no password values in telemetry
* no unnecessary network service
* no cloud backend required
* no external server required
* no analytics by default
* application works completely offline except when connecting to the user's VMs
* no hidden telemetry
* principle of least privilege
* do not request administrator rights unless absolutely required

Installer should not require elevated privileges during normal application use.

============================================================
32. OFFLINE-FIRST
=================

The VM library must function fully without internet.

The app needs internet/network only when connecting to remote VMs.

Do not create:

* web server
* cloud database
* cloud API
* mandatory login
* subscription system

============================================================
33. PERFORMANCE
===============

The application must remain responsive with:

100 VMs
500 VMs
1000 VMs

Do not instantiate every RDP control for every saved VM.

Only create active RDP session controls for connected sessions.

Use virtualization for VM lists/grids where appropriate.

The RDP session UI must be created only when the session is active.

============================================================
34. MEMORY MANAGEMENT
=====================

Remote Desktop COM controls are unmanaged resources.

Properly release:

* COM objects
* RDP ActiveX controls
* WindowsFormsHost
* unmanaged handles

When closing a session:

Disconnect
Dispose session
Release COM objects
Release host control
Remove UI references

Avoid event-handler memory leaks.

============================================================
35. THREADING / STA
===================

The RDP ActiveX control requires appropriate Windows UI/COM apartment handling.

Respect STA requirements.

Do not manipulate ActiveX controls from arbitrary background threads.

Use Dispatcher correctly.

Network/status operations may run asynchronously, but UI/COM operations must occur on the appropriate UI thread.

============================================================
36. UX DETAILS
==============

Modern desktop behavior.

Support:

Enter:
Connect selected VM

Delete:
Delete selected VM

Ctrl+F:
Focus search

Ctrl+N:
New VM

Ctrl+E:
Edit VM

Ctrl+R:
Refresh

Ctrl+1:
Tile view

Ctrl+2:
List view

Double-click VM:
Connect

Right click:
Context menu

Drag VM:
Reorder/group

Remember:

* window size
* window position
* selected view
* selected group
* splitter positions
* theme
* column settings

============================================================
37. STARTUP EXPERIENCE
======================

Startup screen should be fast.

Show:

VMDesk logo
Loading VM library...

Do not unnecessarily block startup.

Load database asynchronously where possible.

If database migration fails:

show recovery UI.

Do not delete or recreate the database automatically.

============================================================
38. FIRST RUN
=============

On first launch:

Display simple welcome page.

Example:

Welcome to VMDesk

Manage your remote VMs in one place.

[Add VM]
[Import VMs]
[Continue]

Do not require account creation.

============================================================
39. SAMPLE DATA
===============

Do not ship production users' credentials.

For development/debug builds only, provide optional sample VMs with:

localhost
192.168.1.100
10.0.0.10

but NEVER provide sample passwords.

Production installer must not include fake credentials.

============================================================
40. PROJECT STRUCTURE
=====================

Use a maintainable solution structure.

Example:

VMDesk.sln

src/
VMDesk.App/
App.xaml
App.xaml.cs
MainWindow.xaml
MainWindow.xaml.cs

```
VMDesk.Core/
    Entities/
    Enums/
    Interfaces/
    Models/
    Services/

VMDesk.Application/
    Services/
    Commands/
    Queries/
    DTOs/

VMDesk.Infrastructure/
    Persistence/
    CredentialStore/
    Logging/
    Configuration/

VMDesk.Rdp/
    Interfaces/
    Microsoft/
    Interop/

VMDesk.UI/
    Views/
    ViewModels/
    Controls/
    Converters/
    Themes/
    Resources/
    Behaviors/

VMDesk.Tests/
    Unit/
    Integration/
```

installer/
VMDesk.iss

docs/
architecture.md
development.md
security.md
troubleshooting.md

============================================================
41. DESIGN PATTERNS
===================

Use appropriately:

* MVVM
* Repository where justified
* Factory
* Strategy
* Adapter
* Observer/event pattern
* Dependency Injection
* Options pattern

Avoid unnecessary abstraction.

Do not create interfaces for every trivial class.

============================================================
42. RDP ABSTRACTION
===================

The application must not directly depend on ActiveX from ViewModels.

Bad:

ViewModel -> MsRdpClient

Good:

ViewModel
|
IRemoteSessionService
|
IRdpSession
|
MicrosoftRdpSession
|
RDP ActiveX

============================================================
43. SESSION MANAGER
===================

Create:

IRemoteSessionManager

Responsibilities:

Create session
Connect
Disconnect
Reconnect
Close session
Get active sessions
Get active session by VM ID
Tile sessions
Activate session

Maintain:

ObservableCollection<RemoteSessionViewModel>

Do not allow duplicate sessions for the same VM unless explicitly configured.

Default behavior:

If VM already has active session:
activate existing session instead of creating duplicate.

============================================================
44. WORKSPACE LAYOUT ENGINE
===========================

Create:

IWorkspaceLayoutService

Input:

number of sessions
window size
available workspace

Output:

layout rectangles.

Supported layouts:

1
2 horizontal
2 vertical
4 grid
6 grid
custom future

The layout engine should not know anything about RDP.

============================================================
45. SESSION VIEW
================

Create:

RemoteSessionView.xaml

It should contain:

* RDP ActiveX host
* session overlay
* loading state
* error state
* toolbar
* connection status

When disconnected:

Show:

Connection lost

[Reconnect]
[Close]

============================================================
46. FAVORITE ICONS / VM ICONS
=============================

Support configurable icon type.

Built-in icons:

Windows
Linux
Server
Database
Cloud
Desktop
Laptop
Custom

No external internet icon dependency.

============================================================
47. THEMING
===========

Create a centralized resource system.

Themes:

Dark
Light

Do not hardcode colors in individual views.

Use resource dictionaries.

All controls must visually work in both themes.

============================================================
48. ACCESSIBILITY
=================

Provide:

* keyboard navigation
* automation names
* accessible button labels
* reasonable focus indicators
* usable font sizes
* no color-only status indicators

============================================================
49. APP SETTINGS STORAGE
========================

Use SQLite for persistent application configuration where appropriate.

Small UI settings may use a local configuration file if justified.

Never store credentials there.

============================================================
50. INSTALLER
=============

Produce a real Windows installer.

Preferred:

Inno Setup.

Output:

VMDesk-Setup-x64.exe

Installer must:

* install application
* create Start Menu shortcut
* optional desktop shortcut
* create uninstall entry
* install correct native/runtime dependencies
* install self-contained application
* preserve user data during uninstall unless user explicitly selects data removal
* support upgrade over existing version
* create appropriate application folder
* cleanly uninstall application binaries

Application binaries:

C:\Program Files\VMDesk\

User data:

%LOCALAPPDATA%\VMDesk\

Do not delete user data automatically during normal uninstall.

Provide optional checkbox:

"Remove VMDesk user data"

============================================================
51. SELF-CONTAINED PUBLISH
==========================

Build self-contained x64 Windows output.

Do not require the user to install .NET separately.

Publish command should conceptually produce:

win-x64
self-contained

If single-file packaging is compatible with all required COM/ActiveX integration, use it.

If single-file packaging creates compatibility problems with the RDP ActiveX dependency, use a self-contained application folder and let Inno Setup create the single installer EXE.

Correctness is more important than forcing a single application binary.

============================================================
52. VERSIONING
==============

Use semantic versioning:

MAJOR.MINOR.PATCH

Example:

1.0.0

Display version in About.

Use centralized version properties.

============================================================
53. ICON
========

Create a professional VM/remote desktop application icon.

Do not use copyrighted Microsoft Remote Desktop branding.

The application should be inspired by remote desktop management UX but have its own identity.

============================================================
54. ABOUT WINDOW
================

Display:

VMDesk
Version x.x.x

Remote VM Manager for Windows

Technology:
.NET
WPF
Microsoft RDP

Buttons:

Documentation
Open Logs Folder
Check for Updates

Do not implement update service unless explicitly required.
The initial release can provide the update mechanism architecture only.

============================================================
55. AUTOMATIC UPDATE ARCHITECTURE
=================================

Create an abstraction:

IUpdateService

but do not require a server.

Implement:

NoUpdateService

for version 1.0.

Architecture should allow future implementation using GitHub Releases or another provider.

============================================================
56. DIAGNOSTICS WINDOW
======================

Create a Diagnostics page.

Display:

Application version
OS version
.NET runtime
RDP ActiveX availability
Database path
Database status
Credential store availability
Network adapter status

Buttons:

Test RDP component
Test database
Open logs

============================================================
57. ROBUSTNESS
==============

The application must handle:

* database locked
* corrupt database
* missing credential
* expired credential
* invalid hostname
* DNS failure
* connection timeout
* RDP disconnect
* computer sleep/wake
* network disconnect/reconnect
* VM deleted while saved in library
* malformed import file
* unsupported RDP setting
* ActiveX initialization failure
* COM exceptions

Never crash the application because a single VM connection failed.

============================================================
58. TESTING
===========

Write unit tests for:

VM CRUD
Search
Filtering
Tagging
Favorites
Groups
Import/export
Backup/restore
Credential reference generation
Connection profile mapping
Workspace layout calculations
Session manager logic
Settings persistence

Integration tests for:

SQLite
Database migrations
Credential store abstraction

For RDP:

Create testable abstraction.

Do not require an actual RDP server for unit tests.

Provide optional integration test mode for a real RDP server.

============================================================
59. BUILD VALIDATION
====================

The agent must actually run:

dotnet restore

dotnet build

dotnet test

dotnet publish

installer build

Resolve all errors.

Do not stop at "the code looks correct."

============================================================
60. STATIC ANALYSIS
===================

Enable:

nullable
analyzers
warnings

Fix:

* compiler errors
* nullability errors
* obvious analyzer violations
* resource leaks

============================================================
61. UI TESTING
==============

Verify manually or through available UI automation:

1. Launch application
2. Add VM
3. Save VM
4. Restart application
5. VM remains
6. Edit VM
7. Save credential
8. Credential remains protected
9. Search VM
10. Favorite VM
11. Switch tile/list
12. Open VM
13. Connect RDP
14. Copy/paste
15. Resize window
16. Smart sizing
17. Fullscreen
18. Multiple simultaneous sessions
19. Tile sessions
20. Disconnect
21. Reconnect
22. Remove VM
23. Restart application
24. Verify VM library remains intact

============================================================
62. SECURITY TESTING
====================

Verify using code review/tests that:

Passwords do not exist in SQLite.

Passwords do not appear in:

logs
JSON export
exceptions
debug output
telemetry
window titles

Verify credential reference is deterministic only where required.

Prefer VM GUID-generated credential targets.

============================================================
63. DATA MIGRATIONS
===================

Use EF Core migrations.

The application must automatically migrate an existing database forward.

Do not destroy existing user data.

Future schema changes must be backwards-conscious.

============================================================
64. DOCUMENTATION
=================

Create:

README.md

Include:

* What VMDesk is
* Features
* Architecture
* Development requirements
* Building
* Running
* Publishing
* Installer generation
* Database location
* Credential storage
* RDP dependency
* Troubleshooting

Create:

docs/architecture.md

Create:

docs/security.md

Create:

docs/development.md

Create:

docs/troubleshooting.md

============================================================
65. DEVELOPMENT REQUIREMENTS
============================

The README must clearly state any prerequisites required to BUILD the software.

The final USER machine should not require the developer toolchain.

============================================================
66. IMPORTANT NATIVE RDP RULE
=============================

Do not implement fake RDP functionality.

Do not simulate remote desktops using screenshots.

Do not launch mstsc.exe as the primary UI implementation.

The application must embed and manage the Microsoft RDP ActiveX control so sessions exist inside the application's own UI.

Launching mstsc.exe can optionally be provided as:

"Open with Windows Remote Desktop"

but it must NOT be the primary experience.

============================================================
67. RDP FEATURE PRIORITY
========================

Priority 1:

* Connect
* Disconnect
* Reconnect
* Authentication
* Clipboard
* Smart sizing
* Resize
* Fullscreen
* Keyboard
* Ctrl+Alt+Del
* Multiple sessions
* Tab view
* Tile view

Priority 2:

* Audio
* Drive redirection
* Printer redirection
* Smart card
* Gateway
* Multi-monitor
* Performance configuration

Priority 3:

* Advanced profiles
* Import/export
* Diagnostics
* Future SSH/VNC support

============================================================
68. IMPORTANT SCOPE RULE
========================

For version 1:

This application is primarily a REMOTE VM CONNECTION MANAGER.

Do NOT build cloud-provider VM provisioning unless required.

The "VM" entity should be provider-neutral metadata.

Architecture should allow future adapters:

IComputeProvider

Possible future implementations:

HyperVProvider
VMwareProvider
AWSProvider
AzureProvider

But version 1 should focus on:

VM library
saved credentials
RDP connections
remote desktop workspace
session management.

============================================================
69. POLISHED UX
===============

The application should feel like a real commercial Windows application.

Avoid:

* huge buttons
* primitive forms
* default WPF styling
* excessive gradients
* unnecessary animations
* web-app-like UI
* poor spacing
* debug-looking controls

Use:

* compact sidebar
* clear hierarchy
* clean cards
* subtle borders
* consistent typography
* keyboard-first interactions
* proper empty states
* proper loading states
* proper error states

============================================================
70. EMPTY STATES
================

When no VMs exist:

Show:

"No VMs yet"

"Add your first VM to get started."

[Add VM]

[Import]

============================================================
71. ERROR STATES
================

Do not show empty broken screens.

Every important failure must have:

What happened

Why it may have happened

Action

Example:

"Unable to connect to 10.0.0.5"

"The remote computer did not accept the connection."

[Retry]

[Edit VM]

[Close]

============================================================
72. FINAL BUILD ARTIFACTS
=========================

At the end, produce:

artifacts/

VMDesk-Setup-x64.exe

VMDesk-Portable-x64.zip

VMDesk-SelfContained/

VMDesk-Symbols/

VMDesk-Source.zip

Also produce:

SHA256SUMS.txt

============================================================
73. FINAL ACCEPTANCE CRITERIA
=============================

The project is considered complete only when:

1. Solution builds successfully.
2. Unit tests pass.
3. Application launches.
4. Database is created automatically.
5. VM can be created.
6. VM persists after restart.
7. Credential is securely stored using Windows Credential Manager.
8. VM can connect through embedded Microsoft RDP.
9. Clipboard works.
10. Smart resize works.
11. Fullscreen works.
12. Multiple RDP sessions work simultaneously.
13. Tile mode works.
14. List mode works.
15. Search works.
16. Favorites work.
17. Groups/tags work.
18. Import/export works.
19. Backup/restore works.
20. App settings persist.
21. Application handles failed connections gracefully.
22. Installer builds successfully.
23. Installed application runs on a clean Windows x64 machine without requiring separate .NET installation.
24. Uninstaller works.
25. User data is preserved during normal uninstall.
26. No passwords are stored in plaintext anywhere.
27. No credentials are written to logs.
28. No external server is required for the application to function.

============================================================
74. AGENT EXECUTION INSTRUCTION
===============================

Work directly in the repository.

First inspect the existing repository/environment.

Then:

1. Create/modify the solution.
2. Implement the architecture.
3. Implement the database.
4. Implement credential storage.
5. Implement RDP integration.
6. Implement UI.
7. Implement session manager.
8. Implement tile/list workspace.
9. Implement settings.
10. Implement import/export.
11. Implement backup/restore.
12. Implement diagnostics.
13. Implement tests.
14. Implement installer.
15. Build.
16. Test.
17. Fix errors.
18. Build installer again.
19. Verify final artifacts.

Do not leave TODO placeholders for core functionality.

Do not create fake implementations merely to make tests pass.

Where the Windows RDP ActiveX API has version-specific interfaces, detect the highest supported interface at runtime and gracefully fall back to an earlier supported interface.

Do not assume every Windows installation exposes exactly the same optional RDP interface version.

Make the application robust against those differences.

============================================================
75. FINAL OUTPUT TO ME
======================

When implementation is finished, report:

Project location

Build status

Test status

Installer location

Portable build location

Number of projects

Database technology

Credential storage mechanism

RDP implementation

Supported RDP features

Known limitations

Any Windows-specific prerequisites

Do not claim a feature works unless it was actually implemented and tested.

END OF SPECIFICATION.

NOW BUILD THE APPLICATION.

# VMDesk — MASTER SPECIFICATION ADDENDUM

# These requirements are mandatory and override conflicting earlier requirements.

============================================================
76. SESSION DISPLAY MODE — EMBEDDED OR SEPARATE WINDOW
======================================================

Every RDP connection must support two display modes.

MODE 1 — EMBEDDED

The RDP session is rendered inside the main VMDesk application workspace.

Example:

Main Window
├── VM Library
└── Session Workspace
├── VM-A
├── VM-B
└── VM-C

MODE 2 — SEPARATE WINDOW

The RDP session is opened in its own native Windows window.

Example:

VMDesk Main Window

and separately:

┌──────────────────────────────────────────────┐
│ VM-A — Remote Desktop                  ▢ ✕  │
├──────────────────────────────────────────────┤
│                                              │
│                                              │
│              REMOTE DESKTOP                  │
│                                              │
│                                              │
└──────────────────────────────────────────────┘

The separate window should behave like a conventional Windows Remote Desktop window.

IMPORTANT:

The separate window must NOT create a second RDP connection.

The same RemoteSession object must be transferred/re-hosted between:

EmbeddedSessionHost
and
StandaloneSessionWindow

Architecture:

IRemoteSession
|
+---- EmbeddedSessionHost
|
+---- StandaloneSessionWindow

============================================================
77. SESSION VIEW MODE TOGGLE
============================

Provide a visible action:

"Open in Window"

when an embedded session is active.

Provide:

"Embed in VMDesk"

when a session is in standalone mode.

Also provide:

"Open in New Window"

from the VM context menu.

Options:

Open in VMDesk
Open in New Window

Remember the user's preferred mode per VM.

VM property:

PreferredSessionDisplayMode

Values:

Embedded
SeparateWindow

Default:

Embedded

Also allow a global default:

Default session display mode:
Embedded
Separate Window

============================================================
78. WINDOW BEHAVIOR
===================

Standalone RDP session windows must support:

* maximize
* minimize
* restore
* resize
* move
* fullscreen
* always-on-top optional
* multiple independent windows
* normal Windows taskbar presence
* individual window title
* individual window icon

Window title format:

VMDesk — <VM Name>

Example:

VMDesk — Production Server

The standalone window must have its own:

* size
* position
* monitor
* fullscreen state

Persist these settings per VM.

============================================================
79. MOVE SESSION BETWEEN EMBEDDED AND WINDOW
============================================

The user must be able to move a live session between modes without disconnecting.

Example:

Embedded
↓
Open in Window
↓
Standalone

Then:

Standalone
↓
Embed in VMDesk
↓
Embedded

Do NOT disconnect/reconnect merely to change presentation mode.

The same remote session must remain alive.

If technical limitations of the ActiveX host require recreating the visual host, preserve the underlying session where possible and clearly separate:

RDP connection lifecycle

from:

UI host lifecycle.

============================================================
80. WINDOWS 11 VISUAL DESIGN
============================

The application must visually follow modern Windows 11 design principles.

Use a Windows 11 / Fluent 2 inspired design language.

IMPORTANT:

Do NOT use an old-fashioned WPF appearance.

Avoid:

* classic square buttons
* heavy gradients
* outdated Windows 7 styling
* dense old-style menus
* excessive glossy effects

Use:

* rounded corners
* modern spacing
* subtle borders
* modern typography
* Fluent-style icons
* modern toggle switches
* modern navigation
* compact command bars
* modern context menus
* Windows 11 style cards
* appropriate visual hierarchy

Support:

Light Theme
Dark Theme
System Theme

Use Windows 11-inspired:

* Mica-like application backdrop where technically appropriate
* Acrylic/translucent surfaces where appropriate
* subtle elevation
* rounded cards
* rounded dialogs
* modern command buttons
* modern navigation sidebar

Do not make the interface resemble a web application.

It must feel like a native Windows 11 desktop application.

============================================================
81. MATERIAL / FLUENT RESOURCE SYSTEM
=====================================

All visual styling must be centralized.

Do not hardcode visual properties throughout XAML.

Create:

Themes/
Light.xaml
Dark.xaml
Shared.xaml
Controls.xaml

Centralize:

* colors
* typography
* spacing
* corner radius
* shadows
* border thickness
* button styles
* card styles
* menus
* dialogs
* text styles

Support dynamic theme switching without restarting the application.

============================================================
82. PER-VM CONNECTION TIMEOUT
=============================

Every VM must have configurable connection timeout settings.

Add to VirtualMachine:

ConnectionTimeoutSeconds

Optional:

ConnectionRetryCount
ConnectionRetryDelaySeconds
ReconnectTimeoutSeconds

Recommended defaults:

ConnectionTimeoutSeconds = 30
ConnectionRetryCount = 1
ReconnectTimeoutSeconds = 30

User can configure these independently for every VM.

UI:

Connection
Timeout: [ 30 ] seconds

Advanced:

Retry count: [ 1 ]
Retry delay: [ 5 ] seconds

Validation:

Minimum timeout:
5 seconds

Maximum timeout:
600 seconds

If a timeout occurs, cancel the pending connection operation where supported and report:

"Connection timed out."

Do not freeze the UI.

============================================================
83. GLOBAL TIMEOUT DEFAULT
==========================

Application Settings must contain:

Default Connection Timeout
Default Retry Count
Default Retry Delay

When creating a new VM:

Use global defaults.

After creation:

VM-specific settings become independent.

Changing global defaults must NOT unexpectedly overwrite existing VM settings.

============================================================
84. CONNECTION TIMEOUT UX
=========================

During connection:

Connecting to <VM Name>...

Elapsed: 12s / 30s

Provide:

Cancel

When timeout occurs:

Connection timed out after 30 seconds.

Actions:

Retry
Edit Connection
Close

============================================================
85. FILE COPY / PASTE
=====================

File transfer must be treated as a first-class feature.

The application must support, wherever supported by the underlying RDP session:

LOCAL → REMOTE

Copy file
Paste file

REMOTE → LOCAL

Copy file
Paste file

Support:

* single files
* multiple files
* folders where the RDP/Windows shell supports the operation

Do not log file names or file contents unnecessarily.

============================================================
86. CLIPBOARD DATA TYPES
========================

Support clipboard redirection for:

* plain text
* Unicode text
* images where supported
* file lists / file-drop clipboard data where supported

The implementation must distinguish:

Text clipboard

from:

File clipboard

The application must NOT treat a file as plaintext clipboard data.

============================================================
87. DRAG AND DROP
=================

Provide drag-and-drop file transfer support.

LOCAL → REMOTE:

User should be able to drag files from Windows Explorer toward the remote desktop/session.

Example:

Windows Explorer
↓ drag
VMDesk RDP session
↓
Remote destination

REMOTE → LOCAL:

Where technically supported, allow dragging files from the remote environment toward the local environment.

The implementation should use native Windows/RDP mechanisms wherever possible.

Do NOT fake file transfer with screenshots.

Do NOT upload files through an external/cloud service.

============================================================
88. RDP DRIVE REDIRECTION
=========================

Expose per-VM drive redirection settings.

Example:

Redirection:

[x] Local drives
[x] Clipboard
[ ] Printers
[ ] Smart cards

Allow the user to select:

All local drives

or:

Specific drives

Example:

C:
D:
E:
USB removable drives

The user must explicitly enable drive redirection.

Default:

Clipboard = Enabled

Drive redirection = Disabled

============================================================
89. FILE TRANSFER TOOLBAR
=========================

Add a session toolbar menu:

File Transfer

Actions:

Copy File
Paste File
Upload Files
Download Files
Open Remote Drives
Enable Drive Redirection
Disable Drive Redirection

Where native RDP clipboard/drive redirection is sufficient, use it.

Where direct drag/drop cannot be safely implemented by the ActiveX control, provide a clear fallback using redirected drives or an integrated file transfer interface.

Never claim direct remote drag/drop if the underlying Windows RDP control does not support that specific operation.

============================================================
90. FILE TRANSFER UX
====================

When transferring larger files, show progress where possible.

Example:

Uploading:

report.zip
████████████████░░░░
78%

Display:

* file name
* direction
* size
* progress
* speed where measurable
* cancel

Do not block the remote desktop session while file transfer is occurring.

============================================================
91. FILE TRANSFER SAFETY
========================

Never:

* execute files automatically
* alter file extensions
* silently overwrite local files
* silently overwrite remote files

If a destination file exists:

Replace
Skip
Keep Both
Cancel

should be available where appropriate.

============================================================
92. DRAG-DROP TARGET UI
=======================

When a local file is dragged over the remote session, display an unobtrusive visual indication:

"Drop to transfer"

Do not intercept normal mouse movement when no file is being dragged.

Normal RDP mouse behavior must remain unaffected.

============================================================
93. SESSION CONTEXT MENU
========================

Right-clicking the active session should provide:

Copy
Paste
Copy File
Paste File
Upload Files
Download Files
Clipboard
Drive Redirection

Display
Smart Fit
Native Size
Fullscreen
Multi Monitor

Session
Reconnect
Disconnect
Send Ctrl+Alt+Del
Open in New Window
Embed in VMDesk

============================================================
94. VM CONTEXT MENU
===================

VM library context menu:

Connect
Connect in VMDesk
Connect in New Window
Reconnect
Disconnect
Open Session
Open in New Window
Embed in VMDesk
Edit
Duplicate
Test Connection
Favorite
Copy Host
Copy Username
Copy Password
Export
Delete

IMPORTANT:

"Copy Password" should require deliberate user interaction.

Do not reveal passwords in tooltip, logs, status bar, or normal UI.

============================================================
95. KEYBOARD SHORTCUTS
======================

Add:

Ctrl+Shift+N
Open current session in new window

Ctrl+Shift+E
Embed current session

Ctrl+Shift+F
Fullscreen

Ctrl+Shift+S
Smart sizing

Ctrl+Shift+R
Reconnect

Ctrl+Shift+D
Disconnect

Ctrl+Shift+C
Copy file / clipboard context where appropriate

Ctrl+Shift+V
Paste file / clipboard context where appropriate

Do not interfere with:

Ctrl+C
Ctrl+V
Ctrl+X
Ctrl+A

when keyboard focus belongs to the remote session.

============================================================
96. FILE TRANSFER ARCHITECTURE
==============================

Create:

IFileTransferService

Implement:

RdpClipboardFileTransferService

RdpDriveTransferService

The service should detect what functionality is available.

Architecture:

Session
|
+-- Clipboard
|
+-- Drive Redirection
|
+-- File Transfer Service
|
+-- Clipboard File Transfer
|
+-- Redirected Drive Transfer

Do not mix file transfer logic into the ViewModel.

============================================================
97. CAPABILITY DETECTION
========================

At session initialization determine:

ClipboardSupported
FileClipboardSupported
DriveRedirectionSupported
SmartSizingSupported
MultiMonitorSupported
AudioRedirectionSupported
PrinterRedirectionSupported
GatewaySupported

Expose capabilities to the UI.

Unsupported features must be:

disabled
or
displayed with an explanatory tooltip.

Do not display controls that appear usable when the underlying RDP implementation cannot support them.

============================================================
98. PER-VM REDIRECTION PROFILE
==============================

Add:

RedirectionProfile

Properties:

ClipboardEnabled
FileClipboardEnabled
DriveRedirectionEnabled
AudioEnabled
MicrophoneEnabled
PrinterEnabled
SmartCardEnabled
PortRedirectionEnabled

Allow:

Default Profile
Custom Profile

VMs can override the global default.

============================================================
99. REMOTE FILE OPERATIONS
==========================

If practical with the native RDP environment, support:

* open remote redirected drive
* local-to-remote copy
* remote-to-local copy
* file upload
* file download

Avoid pretending that the RDP protocol is a generic file synchronization protocol.

Use Windows/RDP-supported mechanisms.

============================================================
100. SESSION HOST ABSTRACTION
=============================

Create:

ISessionHost

Implement:

EmbeddedSessionHost
StandaloneWindowSessionHost

SessionManager must not care where a session is displayed.

Architecture:

RemoteSession
|
+--- EmbeddedSessionHost
|
+--- StandaloneWindowSessionHost

This is mandatory for clean support of both modes.

============================================================
101. MULTI-WINDOW SESSION MANAGEMENT
====================================

Support:

10+ simultaneous sessions if system resources permit.

Each standalone session must have:

* independent window
* independent title
* independent taskbar representation
* independent fullscreen state

Main application must continue running if one standalone session disconnects.

Closing the main VMDesk window must have a configurable behavior:

Option 1:
Close all sessions

Option 2:
Keep standalone sessions open

Default:

Ask the user.

============================================================
102. SESSION LIFECYCLE
======================

Separate these states:

RDP Connection Lifecycle:

Disconnected
Connecting
Connected
Disconnecting
ReconnectPending
Failed

UI Host Lifecycle:

Embedded
Detaching
Standalone
Attaching
Closed

Changing Embedded ↔ Standalone must not be interpreted as disconnecting the remote machine.

============================================================
103. PERFORMANCE REQUIREMENTS
=============================

RDP controls must only exist for active sessions.

Do NOT create an RDP ActiveX instance for every saved VM.

Example:

1000 saved VMs
+
3 active connections

Expected active RDP controls:

3

not:

1000.

============================================================
104. WINDOWS 11 WINDOW MANAGEMENT
=================================

Standalone session windows must support modern Windows behavior.

Use:

* correct DPI scaling
* per-monitor DPI awareness
* monitor-aware placement
* snap support where Windows provides it
* maximize/restore
* correct taskbar behavior
* monitor-aware fullscreen

When opening a standalone session:

Prefer the monitor containing the cursor or the monitor selected by the user.

Persist window placement when safe.

If a saved monitor is no longer available, gracefully move the window to the primary display.

============================================================
105. UPDATED ACCEPTANCE CRITERIA
================================

Version 1 is incomplete unless all of these work:

1. Embedded RDP session
2. Standalone RDP window
3. Embedded ↔ standalone switching
4. Multiple embedded sessions
5. Multiple standalone sessions
6. Windows 11 visual theme
7. Light theme
8. Dark theme
9. System theme
10. Per-VM connection timeout
11. Global connection timeout defaults
12. Clipboard text copy/paste
13. Clipboard image support where native RDP supports it
14. File copy/paste where native RDP supports it
15. File drag/drop where the native Windows/RDP stack permits it
16. Drive redirection
17. Upload/download fallback through redirected drives
18. Smart resizing
19. Fullscreen
20. Multi-monitor where supported
21. Ctrl+Alt+Del
22. Reconnect
23. Connection timeout/cancel
24. Separate session settings
25. Proper Windows 11 styling
26. Secure credential storage
27. No password logging
28. Installer
29. Clean uninstall
30. All existing VM library functionality

============================================================
106. IMPORTANT IMPLEMENTATION PRINCIPLE
=======================================

Prioritize correctness over pretending that every feature is natively supported.

For example:

If native RDP supports file clipboard transfer:
use it.

If native RDP supports local drive redirection:
use it.

If true Explorer-style arbitrary drag-and-drop between local Explorer and the embedded ActiveX desktop is not exposed safely:
provide an integrated file transfer/drop mechanism backed by supported RDP facilities rather than implementing an unreliable fake solution.

Never advertise unsupported behavior as working.

============================================================
107. FINAL DESIGN GOAL
======================

The resulting application should feel like:

"Microsoft Remote Desktop + a professional VM library + multi-session workspace"

with:

* Windows 11 native-feeling UI
* saved VMs
* secure credentials
* embedded sessions
* standalone sessions
* multi-session tiling
* clipboard
* file transfer
* drive redirection
* drag/drop where technically supported
* per-VM timeout
* smart resize
* fullscreen
* multi-monitor
* diagnostics
* offline-first local operation
* standalone installer

END OF ADDENDUM.

## FINAL IMPLEMENTATION PRIORITY