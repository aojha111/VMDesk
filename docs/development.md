# Development

Use Windows 10/11 x64 with the .NET 10 SDK. Restore and build the app project,
then run the application tests. The RDP project references the Windows-generated
`MSTSCLib.dll` and `AxMSTSCLib.dll` files under `src/libs`.

Use `scripts\publish.ps1` to produce `dist\VMDesk.exe` (self-contained single
file) and `dist\VMDesk-Setup-x64.exe` (IExpress installer); `SHA256SUMS.txt`
sits alongside them. The `dist/` executables are committed to the repository so
a build is always available without tooling. Install Inno Setup 6 and ensure
`iscc.exe` is on `PATH` to produce alternative Inno-based installers.

Keep COM code inside `VMDesk.Rdp`; do not reference ActiveX types from ViewModels.