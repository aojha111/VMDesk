using System.Windows;
using System.Windows.Media;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Infrastructure.Configuration;
using AppThemeMode = VMDesk.Core.Enums.ThemeMode;
using WControls = System.Windows.Controls;

namespace VMDesk.App.Views;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly ISettingsService _settings;
    private AppThemeMode _selectedTheme;
    private LibraryViewMode _selectedView;

    public SettingsWindow(App app, ISettingsService settings)
    {
        InitializeComponent();
        _app = app;
        _settings = settings;
        _selectedTheme = app.CurrentTheme;
        Loaded += async (_, _) => await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _settings.GetAsync();
        _selectedTheme = settings.Theme;
        // The library reads "library.view" first, so show that value when present.
        _selectedView = await _settings.GetValueAsync("library.view", settings.DefaultView);

        // Set theme radio buttons
        SetThemeRadio(_selectedTheme);
        SetLibraryViewRadio(_selectedView);
        ConfirmDeleteBox.IsChecked = settings.ConfirmBeforeDelete;
    }

    private void SetThemeRadio(AppThemeMode theme)
    {
        foreach (var child in FindVisualChildren<WControls.RadioButton>(this))
        {
            if (child.GroupName == "Theme" && child.Tag is string tag && Enum.TryParse<AppThemeMode>(tag, out var t))
            {
                child.IsChecked = t == theme;
            }
        }
    }

    private void SetLibraryViewRadio(LibraryViewMode view)
    {
        foreach (var child in FindVisualChildren<WControls.RadioButton>(this))
        {
            if (child.GroupName == "LibraryView" && child.Tag is string tag && Enum.TryParse<LibraryViewMode>(tag, out var v))
            {
                child.IsChecked = v == view;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t) yield return t;

            foreach (T childOfChild in FindVisualChildren<T>(child))
                yield return childOfChild;
        }
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is WControls.RadioButton rb && rb.Tag is string tag && Enum.TryParse<AppThemeMode>(tag, out var theme))
        {
            _selectedTheme = theme;
        }
    }

    private void LibraryViewRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is WControls.RadioButton rb && rb.Tag is string tag && Enum.TryParse<LibraryViewMode>(tag, out var view))
        {
            _selectedView = view;
        }
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        ApplyButton.IsEnabled = false;
        try
        {
            var settings = await _settings.GetAsync();
            settings.Theme = _selectedTheme;
            settings.DefaultView = _selectedView;
            settings.ConfirmBeforeDelete = ConfirmDeleteBox.IsChecked == true;
            await _settings.SaveAsync(settings);
            await _settings.SetValueAsync("library.view", _selectedView);
            _app.ApplyTheme(_selectedTheme);
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }
}