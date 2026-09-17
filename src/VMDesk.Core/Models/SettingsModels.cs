using VMDesk.Core.Enums;

namespace VMDesk.Core.Models;

/// <summary>Global application settings persisted in SQLite (spec §28, §77, §83).</summary>
public sealed record AppSettingsModel
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public LibraryViewMode DefaultView { get; set; } = LibraryViewMode.Tile;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ReconnectSessions { get; set; } = true;
    public bool ConfirmBeforeDelete { get; set; } = true;
    public SessionDisplayMode DefaultSessionDisplayMode { get; set; } = SessionDisplayMode.Embedded;
    public MainCloseAction CloseMainAction { get; set; } = MainCloseAction.Ask;
    public int DefaultConnectionTimeoutSeconds { get; set; } = 30;
    public int DefaultRetryCount { get; set; } = 1;
    public int DefaultRetryDelaySeconds { get; set; } = 5;
    public LogLevelOption LogLevel { get; set; } = LogLevelOption.Information;
    public bool PollingEnabled { get; set; }
    public int PollingIntervalSeconds { get; set; } = 60;
    public string LastSearch { get; set; } = string.Empty;
    public string WindowStateJson { get; set; } = "{}";
    public string ColumnConfigJson { get; set; } = "{}";
    public string SidebarWidth { get; set; } = "220";
}

/// <summary>Export file container (spec §25). Contains no password material.</summary>
public sealed class ImportExportFile
{
    public const int CurrentVersion = 1;

    public int SchemaVersion { get; set; } = CurrentVersion;
    public string AppVersion { get; set; } = string.Empty;
    public DateTimeOffset ExportedAt { get; set; }
    public List<ImportExportVm> VirtualMachines { get; set; } = new();
    public List<string> Groups { get; set; } = new();
}

public sealed class ImportExportVm
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string? Group { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public bool Favorite { get; set; }
    public string Color { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
}

/// <summary>Main-window persistence (spec §36).</summary>
public sealed record WindowStateModel(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>List-view column persistence (spec §5, §36).</summary>
public sealed record ColumnConfigModel(List<ColumnState> Columns)
{
    public List<ColumnState> Columns { get; set; } = Columns ?? new List<ColumnState>();
}

public sealed record ColumnState(string Name, bool Visible, double Width);
