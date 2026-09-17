namespace VMDesk.Infrastructure.Configuration;

/// <summary>Canonical user-data paths (spec §27, §30): %LOCALAPPDATA%\VMDesk.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VMDesk");

    public static string DatabaseFile => Path.Combine(Root, "vmdesk.db");
    public static string LogsDirectory => Path.Combine(Root, "Logs");
    public static string BackupsDirectory => Path.Combine(Root, "Backups");
    public static string ExportsDirectory => Path.Combine(Root, "Exports");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }
}
