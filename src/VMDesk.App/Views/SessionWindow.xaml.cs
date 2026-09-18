using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Threading;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Application.Services;
using VMDesk.Rdp;
using Microsoft.Win32;
using System.Windows.Input;
using WMedia = System.Windows.Media;
using WControls = System.Windows.Controls;

namespace VMDesk.App.Views;

public partial class SessionWindow : Window
{
    private readonly IRemoteSession _session;
    private readonly RemoteSessionManager _manager;
    private readonly RdpFileTransferService _transfer;
    private WindowsFormsHost? _host;
    private bool _isFullscreen;
    private DispatcherTimer? _statusTimer;

    public SessionWindow(IRemoteSession session, RemoteSessionManager manager, RdpFileTransferService transfer)
    {
        InitializeComponent();
        _session = session;
        _manager = manager;
        _transfer = transfer;

        Loaded += OnLoaded;
        Closed += OnClosed;
        KeyDown += OnKeyDown;

        // Subscribe to session events
        _session.StateChanged += OnSessionStateChanged;
        _session.SessionError += OnSessionError;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTitle();
        SetupStatusTimer();

        if (_session.HostControl is System.Windows.Forms.Control control)
        {
            _host = new WindowsFormsHost
            {
                Child = control,
                Focusable = true
            };
            HostContainer.Child = _host;
            PlaceholderGrid.Visibility = Visibility.Collapsed;
            _session.HostMode = SessionHostMode.Embedded;

            // Focus the RDP control
            HostContainer.Focus();
            control.Focus();
        }
        else
        {
            PlaceholderTitle.Text = "Connection Failed";
            PlaceholderMessage.Text = "The Microsoft Remote Desktop control could not be created.";
            ConnectingSpinner.Visibility = Visibility.Collapsed;

            System.Windows.MessageBox.Show(
                this,
                "The Microsoft Remote Desktop control was not created. Open Diagnostics for the detailed RDP availability report.",
                "Remote Desktop Unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SetupStatusTimer()
    {
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusTimer.Tick += (_, _) => UpdateStatusBar();
        _statusTimer.Start();
    }

    private void UpdateStatusBar()
    {
        TimeText.Text = DateTime.Now.ToString("HH:mm:ss");

        if (_session.State == ConnectionState.Connected && _session.HostControl is System.Windows.Forms.Control)
        {
            // We could get actual resolution from the RDP control if needed
            ResolutionText.Text = "1920 × 1080"; // Placeholder
            ColorDepthText.Text = "32-bit";
        }
    }

    private void UpdateTitle()
    {
        SessionTitleText.Text = _session.VmName;
        SessionSubtitleText.Text = _session.VmId.ToString()[..8] + " · " + DateTime.Now.ToString("HH:mm");
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var brush = e.State switch
            {
                ConnectionState.Connected => new SolidColorBrush(WMedia.Color.FromRgb(0x4C, 0xAF, 0x50)),
                ConnectionState.Connecting => new SolidColorBrush(WMedia.Color.FromRgb(0xFF, 0xB3, 0x00)),
                ConnectionState.Reconnecting => new SolidColorBrush(WMedia.Color.FromRgb(0xFF, 0xB3, 0x00)),
                ConnectionState.Failed => new SolidColorBrush(WMedia.Color.FromRgb(0xEF, 0x53, 0x50)),
                _ => new SolidColorBrush(WMedia.Color.FromRgb(0x9E, 0x9E, 0x9E))
            };

            StatusIndicator.Fill = brush;
            StatusText.Text = e.State switch
            {
                ConnectionState.Connected => "Connected",
                ConnectionState.Connecting => "Connecting...",
                ConnectionState.Reconnecting => "Reconnecting...",
                ConnectionState.Failed => "Connection Failed",
                ConnectionState.Disconnected => "Disconnected",
                _ => "Unknown"
            };

            if (e.State == ConnectionState.Connected)
            {
                PlaceholderGrid.Visibility = Visibility.Collapsed;
                HostContainer.Visibility = Visibility.Visible;
            }
            else if (e.State == ConnectionState.Failed)
            {
                PlaceholderTitle.Text = "Connection Failed";
                PlaceholderMessage.Text = e.Message ?? "Unable to establish connection";
                ConnectingSpinner.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void OnSessionError(object? sender, SessionErrorEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            PlaceholderTitle.Text = "Connection Error";
            PlaceholderMessage.Text = e.FriendlyMessage;
            ConnectingSpinner.Visibility = Visibility.Collapsed;
            PlaceholderGrid.Visibility = Visibility.Visible;
        });
    }

    private async void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // F11 for fullscreen toggle
        if (e.Key == System.Windows.Input.Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        // Ctrl+Alt+End for Ctrl+Alt+Del
        else if (e.Key == System.Windows.Input.Key.End &&
                 (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                 (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            SendCtrlAltDel();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            ConnectionBar.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            FullscreenHint.Visibility = Visibility.Visible;
            FullscreenButton.Content = new WControls.TextBlock { Text = "&#xE73F;", FontFamily = new WMedia.FontFamily("Segoe MDL2 Assets"), FontSize = 14 };
        }
        else
        {
            ConnectionBar.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.CanResize;
            FullscreenHint.Visibility = Visibility.Collapsed;
            FullscreenButton.Content = new WControls.TextBlock { Text = "&#xE740;", FontFamily = new WMedia.FontFamily("Segoe MDL2 Assets"), FontSize = 14 };
        }

        _session.ToggleFullscreen();
    }

    private void SendCtrlAltDel()
    {
        _session.SendCtrlAltDel();
    }

    private async void DisconnectClick(object sender, RoutedEventArgs e)
    {
        await _manager.CloseAsync(_session);
        Close();
    }

    private void ClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        _session.SetClipboardRedirect(true);
        System.Windows.MessageBox.Show(this, "Clipboard redirection is enabled for this session.", "VMDesk", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle audio - would need state tracking
        _session.SetAudioRedirect(true);
        System.Windows.MessageBox.Show(this, "Audio redirection enabled.", "VMDesk", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void SendCtrlAltDelButton_Click(object sender, RoutedEventArgs e)
    {
        SendCtrlAltDel();
    }

    private void FileTransferButton_Click(object sender, RoutedEventArgs e)
    {
        new FastTransferWindow(_session, _transfer) { Owner = this }.ShowDialog();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _statusTimer?.Stop();
        _session.StateChanged -= OnSessionStateChanged;
        _session.SessionError -= OnSessionError;

        if (_manager.FindByVm(_session.VmId) is not null)
        {
            await _manager.CloseAsync(_session);
        }

        // Clean up WindowsFormsHost
        if (_host != null)
        {
            HostContainer.Child = null;
            _host.Dispose();
            _host = null;
        }
    }
}