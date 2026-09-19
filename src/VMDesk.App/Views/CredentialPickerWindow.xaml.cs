using System.Collections.ObjectModel;
using System.Windows;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.App.Views;

/// <summary>
/// Pick a saved credential by name when connecting. Editing stays in the
/// Credential Manager page; this dialog only selects (spec §7).
/// </summary>
public partial class CredentialPickerWindow : Window
{
    private readonly ICredentialStore _store;
    public ObservableCollection<SavedCredential> Credentials { get; } = new();

    public CredentialPickerWindow(ICredentialStore store, string? suggestedReference = null)
    {
        InitializeComponent();
        _store = store;
        DataContext = this;
        Loaded += async (_, _) => await ReloadAsync(suggestedReference);
    }

    /// <summary>The credential the user accepted, or null when cancelled.</summary>
    public SavedCredential? SelectedCredential => CredentialBox.SelectedItem as SavedCredential;

    private async Task ReloadAsync(string? suggestedReference)
    {
        try
        {
            Credentials.Clear();
            foreach (var credential in await _store.GetCredentialsAsync())
            {
                Credentials.Add(credential);
            }

            CredentialBox.ItemsSource = Credentials;
            if (Credentials.Count == 0)
            {
                CredentialBox.IsEnabled = false;
                UseButton.IsEnabled = false;
                CredentialBox.ToolTip = "No saved credentials. Open Manage credentials to add one.";
            }

            if (!string.IsNullOrWhiteSpace(suggestedReference))
            {
                CredentialBox.SelectedItem = Credentials.FirstOrDefault(c =>
                    string.Equals(c.Reference, suggestedReference, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Credential Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ManageClick(object sender, RoutedEventArgs e)
    {
        new CredentialManagerWindow(_store) { Owner = this }.ShowDialog();
        _ = ReloadAsync(SelectedCredential?.Reference);
    }

    private void UseClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCredential is null)
        {
            System.Windows.MessageBox.Show(this,
                "Select a saved credential first.",
                "Choose a credential",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
