using System.Windows;
using System.Windows.Threading;

namespace VMDesk.App.Views;

public partial class ConnectionProgressWindow : Window
{
    private DateTime _startTime;
    private DispatcherTimer? _timer;

    public event EventHandler? CancelRequested;

    public ConnectionProgressWindow()
    {
        InitializeComponent();
        _startTime = DateTime.Now;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateElapsed();
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer?.Stop();
    }

    public void UpdateStatus(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
        });
    }

    public void SetTitle(string vmName)
    {
        Dispatcher.Invoke(() =>
        {
            TitleText.Text = $"Connecting to {vmName}...";
            SubtitleText.Text = "Establishing secure RDP session";
        });
    }

    private void UpdateElapsed()
    {
        var elapsed = DateTime.Now - _startTime;
        Dispatcher.Invoke(() =>
        {
            ElapsedText.Text = $"Elapsed: {(int)elapsed.TotalSeconds}s";
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling...";
        StatusText.Text = "Cancelling connection...";
    }
}