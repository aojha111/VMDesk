
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

VMDesk — Remote VM Manager

The resulting UX should look roughly like:

┌──────────────────────────────────────────────────────────────────┐
│ VMDesk        Search VMs...                    + Add VM   ⚙     │
├───────────────┬──────────────────────────────────────────────────┤
│               │                                                  │
│  All VMs      │   My VMs                          Tile   List   │
│  Favorites    │                                                  │
│  Recent       │   ┌──────────────┐  ┌──────────────┐             │
│  Connected    │   │ 🖥 Dev VM    │  │ 🖥 Prod VM   │             │
│  Groups       │   │ 10.0.0.21    │  │ 10.0.0.31    │             │
│  Tags         │   │ ● Connected  │  │ ○ Offline    │             │
│               │   │ [ Connect ]  │  │ [ Connect ]  │             │
│               │   └──────────────┘  └──────────────┘             │
│               │                                                  │
│  Settings     │   ┌──────────────┐  ┌──────────────┐             │
│               │   │ 🖥 DB VM     │  │ 🖥 Test VM   │             │
│               │   │ 10.0.0.40    │  │ 10.0.0.51    │             │
│               │   │ ● Connected  │  │ ○ Offline    │             │
│               │   └──────────────┘  └──────────────┘             │
└───────────────┴──────────────────────────────────────────────────┘


Then, after connecting:
┌──────────────────────────────────────────────────────────────┐
│ Dev VM │ Prod VM │ DB VM │ Test VM                         │
├───────────────────────────┬──────────────────────────────────┤
│                           │                                  │
│                           │                                  │
│         DEV VM            │             PROD VM              │
│                           │                                  │
│                           │                                  │
├───────────────────────────┼──────────────────────────────────┤
│                           │                                  │
│          DB VM            │             TEST VM              │
│                           │                                  │
└───────────────────────────┴──────────────────────────────────┘


# VMDesk — UI / UX LAYOUT SPECIFICATION

============================================================

1. WINDOWS 11 VISUAL LANGUAGE
   ============================================================

The application should resemble a modern Windows 11 desktop application.

Design language:

* Fluent-inspired
* rounded corners
* Mica-style backdrop where appropriate
* modern Windows typography
* compact controls
* subtle separators
* modern toggles
* modern command bar
* lightweight shadows
* clean cards
* high DPI support

Themes:

System
Light
Dark

Theme changes should apply immediately.

============================================================
2. MAIN WINDOW
==============

┌─────────────────────────────────────────────────────────────┐
│ VMDesk                                     Search     +   ⚙ │
├──────────────┬──────────────────────────────────────────────┤
│              │                                              │
│  ALL VMs     │  All VMs                       TILE | LIST  │
│              │                                              │
│  Favorites   │  ┌───────────┐  ┌───────────┐               │
│  Recent      │  │ Dev VM    │  │ Prod VM   │               │
│  Connected   │  │ ● Online  │  │ ○ Offline │               │
│              │  │ [Connect] │  │ [Connect] │               │
│  Groups      │  └───────────┘  └───────────┘               │
│              │                                              │
│  Tags        │  ┌───────────┐  ┌───────────┐               │
│              │  │ DB VM     │  │ Test VM   │               │
│              │  │ ● Online  │  │ ○ Offline │               │
│              │  └───────────┘  └───────────┘               │
│              │                                              │
│  Settings    │                                              │
└──────────────┴──────────────────────────────────────────────┘

============================================================
3. VM CARD
==========

┌──────────────────────────────────────────┐
│ 🖥  Production Server             ☆      │
│                                          │
│ 10.10.10.21 : 3389                       │
│ administrator                            │
│                                          │
│ ● Connected                              │
│                                          │
│ [ Connect ]                       ⋮      │
└──────────────────────────────────────────┘

Context menu:

Connect
Connect in New Window
Reconnect
Edit
Duplicate
Favorite
Test Connection
Copy Host
Copy Username
Export
Delete

============================================================
4. SESSION WORKSPACE
====================

When the user chooses:

"Open in VMDesk"

the main window changes into:

┌─────────────────────────────────────────────────────────────┐
│ ← Library     Dev VM │ Prod VM │ DB VM             ⛶  ⋮    │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│                     RDP SESSION                             │
│                                                             │
│                                                             │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│ Connected • 10.10.10.21                    Clipboard  ⛶    │
└─────────────────────────────────────────────────────────────┘

Toolbar can auto-hide.

============================================================
5. SESSION TOOLBAR
==================

Toolbar:

[Back]
[VM Name]

[Clipboard]
[Files]
[Smart Fit]
[Native]
[Fullscreen]
[Ctrl+Alt+Del]
[Reconnect]
[Open in Window]
[⋮]

============================================================
6. SESSION WINDOW MODE
======================

When user clicks:

Open in Window

create:

┌───────────────────────────────────────────────────────────────┐
│ VMDesk — Production Server                     ▢ □ ✕         │
├───────────────────────────────────────────────────────────────┤
│                                                               │
│                                                               │
│                  REMOTE DESKTOP                               │
│                                                               │
│                                                               │
└───────────────────────────────────────────────────────────────┘

Toolbar remains available.

Menu:

Embed in VMDesk
Fullscreen
Smart Fit
Native Size
Clipboard
Files
Reconnect
Disconnect

============================================================
7. SESSION MODE TOGGLE
======================

Primary toggle/action:

┌──────────────────────────────────┐
│ Session location                 │
│                                  │
│ ● Inside VMDesk                  │
│ ○ Separate Window                │
│                                  │
│ [Apply]                          │
└──────────────────────────────────┘

Also provide quick command:

Open in Window

and:

Embed in VMDesk

============================================================
8. MULTI SESSION TILE MODE
==========================

Two sessions:

┌────────────────────────┬────────────────────────┐
│                        │                        │
│         VM A           │          VM B          │
│                        │                        │
└────────────────────────┴────────────────────────┘

Four sessions:

┌────────────────────────┬────────────────────────┐
│          VM A          │          VM B          │
│                        │                        │
├────────────────────────┼────────────────────────┤
│          VM C          │          VM D          │
│                        │                        │
└────────────────────────┴────────────────────────┘

Six sessions:

┌───────────────┬───────────────┬───────────────┐
│     VM A      │     VM B      │     VM C      │
├───────────────┼───────────────┼───────────────┤
│     VM D      │     VM E      │     VM F      │
└───────────────┴───────────────┴───────────────┘

============================================================
9. FILE TRANSFER UI
===================

Click:

Files

opens:

┌────────────────────────────────────────────┐
│ File Transfer                             │
├────────────────────────────────────────────┤
│                                            │
│ Local → Remote                             │
│                                            │
│ [ Upload Files ]                           │
│ [ Paste Files ]                            │
│                                            │
│ Remote → Local                             │
│                                            │
│ [ Download Files ]                         │
│                                            │
│ Drive Redirection                          │
│ [ Configure Drives ]                       │
│                                            │
└────────────────────────────────────────────┘

============================================================
10. DRAG-DROP
=============

When a user drags a file over the remote session:

┌────────────────────────────────────────────┐
│                                            │
│          ↓ Drop files to transfer ↓       │
│                                            │
└────────────────────────────────────────────┘

Use a translucent Windows 11-style drop overlay.

The overlay must disappear when the drag operation ends.

============================================================
11. CONNECTION SETTINGS
=======================

VM Edit:

┌──────────────────────────────────────────────┐
│ Connection                                   │
├──────────────────────────────────────────────┤
│ Host                [ 10.10.10.21 ]          │
│ Port                [ 3389 ]                 │
│                                              │
│ Connection timeout  [ 30 ] seconds           │
│ Retry count         [ 1 ]                    │
│ Retry delay         [ 5 ] seconds            │
│                                              │
│ [x] Reconnect automatically                  │
└──────────────────────────────────────────────┘

============================================================
12. REDIRECTION SETTINGS
========================

┌──────────────────────────────────────────────┐
│ Redirection                                   │
├──────────────────────────────────────────────┤
│ [x] Clipboard                                 │
│ [x] File Clipboard                            │
│ [ ] Local Drives                              │
│ [ ] Printers                                  │
│ [ ] Audio                                    │
│ [ ] Microphone                                │
│ [ ] Smart Card                                │
│                                              │
│ Drives                                       │
│ [ Configure ]                                │
└──────────────────────────────────────────────┘

Defaults:

Clipboard = ON
File Clipboard = ON where supported
Drive Redirection = OFF

============================================================
13. DISPLAY SETTINGS
====================

┌──────────────────────────────────────────────┐
│ Display                                      │
├──────────────────────────────────────────────┤
│ Mode                                         │
│ (•) Smart Fit                                │
│ ( ) Native Size                              │
│ ( ) Fullscreen                               │
│                                              │
│ Multi-monitor                                │
│ [ Primary monitor ▼ ]                        │
│                                              │
│ [x] Use all monitors                         │
└──────────────────────────────────────────────┘

============================================================
14. WINDOWS 11 DETAILS
======================

Use consistent:

Corner radius:
8–12 px

Cards:
12–16 px

Dialogs:
12–16 px

Buttons:
8 px

Use subtle separators.

Avoid dense borders.

Use modern navigation selection states.

Hover states should be subtle.

Focus state must be clearly visible.

============================================================
15. EMPTY VM LIBRARY
====================

Centered:

```
    🖥

  No VMs yet
```

Add a remote computer to get started.

```
   [ + Add VM ]

   [ Import ]
```

============================================================
16. CONNECTION ERROR
====================

┌──────────────────────────────────────────────┐
│ Connection failed                            │
├──────────────────────────────────────────────┤
│                                              │
│ Unable to connect to Production Server.     │
│                                              │
│ The connection timed out after 30 seconds.  │
│                                              │
│ [ Retry ]  [ Edit VM ]  [ Close ]            │
│                                              │
│ Show technical details                       │
└──────────────────────────────────────────────┘

============================================================
17. FILE TRANSFER PROGRESS
==========================

┌──────────────────────────────────────────────┐
│ Uploading report.zip                         │
│                                              │
│ ███████████████████░░░  82%                 │
│                                              │
│ 164 MB / 200 MB                              │
│ 18.2 MB/s                                    │
│                                              │
│                         [Cancel]             │
└──────────────────────────────────────────────┘

============================================================
18. OVERALL UX GOAL
===================

The user should be able to:

1. Open VMDesk.
2. See all saved VMs immediately.
3. Double-click a VM.
4. Get an RDP session immediately.
5. Choose whether it lives:

   * inside VMDesk
   * in its own window
6. Copy/paste text.
7. Copy/paste files where supported.
8. Drag files where technically supported.
9. Transfer files using redirected drives when necessary.
10. Resize the session intelligently.
11. Fullscreen it.
12. Run multiple sessions simultaneously.
13. Tile sessions.
14. Move sessions between embedded and separate-window mode.

The result should feel like a modern Windows 11-native evolution of the classic Microsoft Remote Desktop experience.

END OF LAYOUT SPECIFICATION.

## FINAL IMPLEMENTATION PRIORITY