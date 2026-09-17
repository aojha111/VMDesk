namespace VMDesk.Core.Enums;

/// <summary>State of an RDP connection lifecycle (spec §16, §102).</summary>
public enum ConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Disconnecting = 4,
    Failed = 5
}

/// <summary>UI host lifecycle, deliberately separate from connection state (spec §102).</summary>
public enum SessionHostMode
{
    Closed = 0,
    Embedded = 1,
    Detaching = 2,
    Standalone = 3,
    Attaching = 4
}

/// <summary>Display scale mode (spec §11).</summary>
public enum DisplayScaleMode
{
    SmartFit = 0,
    Native = 1,
    Fullscreen = 2
}

public enum PerformancePreset
{
    Automatic = 0,
    Lan = 1,
    Broadband = 2,
    HighLatency = 3,
    LowBandwidth = 4
}

public enum KeyboardHookMode
{
    OnTheFly = 0,
    Remote = 1,
    FullScreenOnly = 2
}

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum LibraryViewMode
{
    Tile = 0,
    List = 1
}

/// <summary>Embedded vs separate native window (spec §77).</summary>
public enum SessionDisplayMode
{
    Embedded = 0,
    SeparateWindow = 1
}

/// <summary>Extensible protocol model; only RDP is implemented (spec §6).</summary>
public enum ProtocolKind
{
    Rdp = 0,
    Ssh = 1,
    Vnc = 2
}

public enum TransferDirection
{
    Upload = 0,
    Download = 1
}

/// <summary>Import conflict handling (spec §25).</summary>
public enum ImportConflictStrategy
{
    Merge = 0,
    CreateNew = 1,
    Skip = 2
}

/// <summary>What happens when the main window closes (spec §101).</summary>
public enum MainCloseAction
{
    Ask = 0,
    CloseAllSessions = 1,
    KeepStandaloneSessions = 2
}

public enum LogLevelOption
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3
}
