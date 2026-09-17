using System.Windows;

namespace VMDesk.App.Views;

public partial class SettingsWindow : Window
{
    private readonly App _app;

    public SettingsWindow(App app)
    {
        InitializeComponent();
        _app = app;
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (ThemeBox.SelectedItem is FrameworkElement item && item.Tag is string tag && Enum.TryParse<VMDesk.Core.Enums.ThemeMode>(tag, out var theme))
        {
            _app.ApplyTheme(theme);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}