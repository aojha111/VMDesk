using System.Collections.ObjectModel;
using System.Windows;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.App.Views;

public partial class CredentialManagerWindow : Window
{
    private readonly ICredentialStore _store;
    public ObservableCollection<SavedCredential> Credentials { get; } = new();
    public SavedCredential? SelectedCredential { get; set; }

    public CredentialManagerWindow(ICredentialStore store)
    {
        InitializeComponent();
        _store = store;
        DataContext = this;
        Loaded += async (_, _) => await RunStoreOperationAsync(ReloadAsync);
    }

    public async Task ReloadAsync()
    {
        Credentials.Clear();
        foreach (var credential in await _store.GetCredentialsAsync()) Credentials.Add(credential);
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UsernameBox.Text) || PasswordBox.Password.Length == 0) return;
        var label = string.IsNullOrWhiteSpace(LabelBox.Text) ? Guid.NewGuid().ToString("N") : LabelBox.Text.Trim();
        var reference = label.StartsWith("VMDesk/", StringComparison.Ordinal) ? label : "VMDesk/" + label;
        await RunStoreOperationAsync(async () =>
        {
            await _store.SaveCredentialAsync(reference, UsernameBox.Text.Trim(), PasswordBox.Password);
            PasswordBox.Clear();
            await ReloadAsync();
        });
    }

    private void SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SelectedCredential = CredentialsList.SelectedItem as SavedCredential;
        if (SelectedCredential is not null)
        {
            LabelBox.Text = SelectedCredential.Reference["VMDesk/".Length..];
            UsernameBox.Text = SelectedCredential.Username;
            PasswordBox.Clear();
            return;
        }

        LabelBox.Clear();
        UsernameBox.Clear();
        PasswordBox.Clear();
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (CredentialsList.SelectedItem is not SavedCredential selected) return;
        await RunStoreOperationAsync(async () =>
        {
            await _store.DeleteCredentialAsync(selected.Reference);
            await ReloadAsync();
            LabelBox.Clear();
            UsernameBox.Clear();
            PasswordBox.Clear();
        });
    }

    private void UseClick(object sender, RoutedEventArgs e)
    {
        if (CredentialsList.SelectedItem is not SavedCredential selected) return;
        SelectedCredential = selected;
        DialogResult = true;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private async Task RunStoreOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Credential Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}