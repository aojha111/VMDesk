# Development

Use Windows 10/11 x64 with the .NET 10 SDK. Restore and build the app project,
then run the application tests. The RDP project references the Windows-generated
`MSTSCLib.dll` and `AxMSTSCLib.dll` files under `src/libs`.

Use `scripts\publish.ps1` for a self-contained x64 folder and portable ZIP.
Install Inno Setup 6 and ensure `iscc.exe` is on `PATH` to produce the installer.

Keep COM code inside `VMDesk.Rdp`; do not reference ActiveX types from ViewModels.