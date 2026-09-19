# Troubleshooting

## The app reports a database table error

Close VMDesk and keep a copy of `%LOCALAPPDATA%\VMDesk`. Current startup repairs
an empty database file without deleting a database containing unknown user tables.
If the database contains partial or corrupt data, use a backup or move the file
aside only after preserving it for recovery.

## RDP is unavailable

The Microsoft Remote Desktop ActiveX control must be registered by Windows. Open
Diagnostics to see the detected control and runtime details. A missing control or
unreachable host is reported as a connection problem; VMDesk does not fake an RDP
desktop.

## Credentials fail

Edit the VM and save the password again, or open the Credential Manager page to
update the stored entry. When connecting a VM that has no usable credential,
VMDesk shows a picker with every saved credential; the choice is remembered on
the VM. Passwords are read from the current Windows user's Credential Manager
profile and are not portable through JSON export.

## Connecting reports "Connection cancelled" for a slow host

Builds before the connect-flow fix labelled a connection timeout as
"Connection cancelled" and could leave a progress window open forever. Rebuild
with `scripts\publish.ps1` or use the current `dist/VMDesk.exe`; timeouts now
retry and report as timeouts, and the progress window closes on its own.

## "Cannot find resource" XAML errors at startup

VMDesk validates all XAML resource references at publish time via
`scripts/verify-xaml.ps1`. If you see a `XamlParseException` reporting
"Cannot find resource named 'NavigationButton'", "LoadingSpinner", or
"ConnectionStateToVisibility'", you are running a build that predates the
fix. Rebuild from the latest source (`scripts/publish.ps1`) or download a
fresh release artifact.

The root cause was two stale resource references in `MainWindow.xaml`:

1. **`LoadingSpinner`** — the resource was a `Style` that no longer existed;
   only `LoadingSpinnerTemplate` (a `ControlTemplate`) was defined in
   `Controls.xaml`. The XAML was updated to use a `ContentControl` with
   `Template="{DynamicResource LoadingSpinnerTemplate}"`.

2. **`ConnectionStateToVisibility`** — the converter class existed in
   `Converters.cs` but was never registered as a resource in
   `MainWindow.xaml`'s `Window.Resources`. The declaration
   `<converters:ConnectionStateToVisibilityConverter x:Key="ConnectionStateToVisibility" />`
   was added.