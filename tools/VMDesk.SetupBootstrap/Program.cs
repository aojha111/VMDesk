using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\..\..\..\.."));
var publishDir = Path.GetFullPath(Path.Combine(repoRoot, "publish_output", "VMDesk-final"));
var artifactsDir = Path.GetFullPath(Path.Combine(repoRoot, "artifacts"));
Directory.CreateDirectory(artifactsDir);

if (!Directory.Exists(publishDir) || !File.Exists(Path.Combine(publishDir, "VMDesk.exe")))
{
    Console.Error.WriteLine($"Missing publish output: {publishDir}");
    Environment.Exit(1);
}

var zipPath = Path.Combine(artifactsDir, "VMDesk-Setup-x64.zip");
if (File.Exists(zipPath)) File.Delete(zipPath);
ZipFile.CreateFromDirectory(publishDir, zipPath, CompressionLevel.Optimal, false);

var exePath = Path.Combine(artifactsDir, "VMDesk-Setup-x64.exe");
if (File.Exists(exePath)) File.Delete(exePath);

var makeCab = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "makecab.exe");
var ddfPath = Path.Combine(artifactsDir, "VMDesk-Setup-x64.ddf");
var cabPath = Path.Combine(artifactsDir, "VMDesk-Setup-x64.cab");

var ddf = $"""
.Set CabinetNameTemplate={cabPath}
.Set CompressionType=LZX
.Set CompressionLevel=7
.Set DiskDirectoryTemplate={artifactsDir}
.Set Cabinet=ON
.Set MaxDiskSize=999999999
.Set RptFileName={Path.Combine(artifactsDir, "VMDesk-Setup-x64.rpt")}
"{zipPath}"
""";
File.WriteAllText(ddfPath, ddf);

var psi = new ProcessStartInfo(makeCab, $"/F \"{ddfPath}\"")
{
    WorkingDirectory = artifactsDir,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
};

var process = Process.Start(psi)!;
process.WaitForExit();
var stdout = process.StandardOutput.ReadToEnd();
var stderr = process.StandardError.ReadToEnd();
if (process.ExitCode != 0)
{
    Console.Error.WriteLine(stderr);
    Environment.Exit(process.ExitCode);
}

if (File.Exists(cabPath))
{
    File.Move(cabPath, exePath, overwrite: true);
}

Console.WriteLine($"Created: {exePath}");
