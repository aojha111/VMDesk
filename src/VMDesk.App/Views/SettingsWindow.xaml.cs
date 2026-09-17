using System.Windows;

namespace VMDesk.App.Views;

public partial class SettingsWindow : Window
{
    private readonly App _app;

    public SettingsWindow(App app)
    {
        InitializeComponent();
        _app = app;
        ThemeBox.SelectedValue = app.CurrentTheme.ToString();
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (ThemeBox.SelectedValue is not string tag || !Enum.TryParse<VMDesk.Core.Enums.ThemeMode>(tag, out var theme)) return;
        ApplyButton.IsEnabled = false;
        try
        {
            await _app.SaveThemeAsync(theme);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Could not save appearance", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}