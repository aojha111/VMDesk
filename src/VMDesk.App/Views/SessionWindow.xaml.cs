using System.Windows;
using System.Windows.Forms.Integration;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Application.Services;
using VMDesk.Rdp;
using Microsoft.Win32;

namespace VMDesk.App.Views;

public partial class SessionWindow : Window
{
    private readonly IRemoteSession _session;
    private readonly RemoteSessionManager _manager;
    private readonly RdpFileTransferService _transfer;

    public SessionWindow(IRemoteSession session, RemoteSessionManager manager, RdpFileTransferService transfer)
    {
        InitializeComponent();
        _session = session;
        _manager = manager;
        _transfer = transfer;
        TitleText.Text = session.VmName;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_session.HostControl is System.Windows.Forms.Control control)
        {
            HostContainer.Children.Add(new WindowsFormsHost { Child = control });
            _session.HostMode = SessionHostMode.Embedded;
        }
        else
        {
            TitleText.Text += "  (RDP control unavailable)";
        }
    }

    private async void DisconnectClick(object sender, RoutedEventArgs e)
    {
        await _manager.CloseAsync(_session);
        Close();
    }

    private void ClipboardClick(object sender, RoutedEventArgs e)
    {
        _session.SetClipboardRedirect(true);
        System.Windows.MessageBox.Show(this, "Clipboard redirection is enabled for this session.", "VMDesk", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyFilesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Choose files to place on the clipboard" };
        if (dialog.ShowDialog() == true) _transfer.CopyFilesToClipboard(dialog.FileNames);
    }

    private void UploadFilesClick(object sender, RoutedEventArgs e) => new FastTransferWindow(_session, _transfer) { Owner = this }.ShowDialog();

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (_manager.FindByVm(_session.VmId) is not null)
        {
            await _manager.CloseAsync(_session);
        }
    }
}