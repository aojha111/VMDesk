using System.Windows;
using Microsoft.Win32;
using VMDesk.Core.Interfaces;
using VMDesk.Rdp;

namespace VMDesk.App.Views;

public partial class FastTransferWindow : Window
{
    private readonly IRemoteSession _session;
    private readonly RdpFileTransferService _transfer;
    private string[] _files = Array.Empty<string>();

    public FastTransferWindow(IRemoteSession session, RdpFileTransferService transfer)
    {
        InitializeComponent();
        _session = session;
        _transfer = transfer;
    }

    private void ChooseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            _files = dialog.FileNames;
            FilesLabel.Text = $"{_files.Length} file(s) selected";
        }
    }

    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (_files.Length == 0 || string.IsNullOrWhiteSpace(RemoteRootBox.Text)) return;
        if (!_transfer.IsDriveTransferAvailable(_session))
        {
            System.Windows.MessageBox.Show(this, "Drive redirection was not enabled for this session. Enable it in the VM settings and reconnect.", "Fast file transfer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var progress = new Progress<VMDesk.Core.Models.TransferProgress>(value => Progress.Value = value.PercentComplete);
            await _transfer.UploadAsync(_session, _files, RemoteRootBox.Text.Trim(), progress, CancellationToken.None);
            FilesLabel.Text = "Transfer completed";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Transfer failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}