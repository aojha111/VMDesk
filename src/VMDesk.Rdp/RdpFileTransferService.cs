using System.IO;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Rdp;

/// <summary>
/// File transfer via the OS clipboard file list + redirected drives (spec §85-91, §96).
/// Copy File / Paste File use the clipboard file format so both sides of the RDP
/// session can paste through the native clipboard channel. Upload/Download copy
/// through a redirected drive path. No fake transfer, no cloud service (spec §106).
/// </summary>
public sealed partial class RdpFileTransferService : IFileTransferService
{
    private readonly IAppLog _log;

    public RdpFileTransferService(IAppLogFactory logFactory)
    {
        _log = logFactory.GetLogger("FileTransfer");
    }

    public bool IsDriveTransferAvailable(IRemoteSession session)
    {
        // Drive redirection is a connection-time option; the session reports the capability.
        return session.Capabilities.DriveRedirectionSupported;
    }

    /// <summary>
    /// Upload: local paths are copied into the target root, which must be inside a
    /// redirected drive visible to the remote session.
    /// </summary>
    public async Task UploadAsync(IRemoteSession session, IReadOnlyList<string> localPaths, string remoteRoot, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteRoot))
        {
            throw new ArgumentException("Remote root is required (a redirected drive path such as \\\\tsclient\\C).", nameof(remoteRoot));
        }

        await CopyWithProgressAsync(localPaths, remoteRoot, TransferDirection.Upload, progress, cancellationToken);
        _log.Info("Upload of " + localPaths.Count + " item(s) completed.");
    }

    public async Task DownloadAsync(IRemoteSession session, IReadOnlyList<string> remotePaths, string localDirectory, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localDirectory))
        {
            throw new ArgumentException("Local directory is required.", nameof(localDirectory));
        }

        Directory.CreateDirectory(localDirectory);
        await CopyWithProgressAsync(remotePaths, localDirectory, TransferDirection.Download, progress, cancellationToken);
        _log.Info("Download of " + remotePaths.Count + " item(s) completed.");
    }

    public void CopyFilesToClipboard(IReadOnlyList<string> localPaths)
    {
        if (localPaths.Count == 0)
        {
            return;
        }

        var paths = new System.Collections.Specialized.StringCollection();
        paths.AddRange(localPaths.ToArray());
        Clipboard.SetFileDropList(paths);
    }

    public IReadOnlyList<string> GetClipboardFiles()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return Array.Empty<string>();
        }

        var list = Clipboard.GetFileDropList();
        var result = new List<string>(list.Count);
        foreach (var item in list)
        {
            if (!string.IsNullOrEmpty(item))
            {
                result.Add(item);
            }
        }

        return result;
    }
}
