namespace VMDesk.Rdp;

/// <summary>Remote desktop size reported by the RDP control (spec §11).</summary>
public sealed class RemoteSizeChangedEventArgs : EventArgs
{
    public RemoteSizeChangedEventArgs(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}