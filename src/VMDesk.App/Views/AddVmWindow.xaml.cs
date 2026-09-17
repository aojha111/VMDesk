using System.Windows;
using System.Collections.ObjectModel;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.App.Views;

public partial class AddVmWindow : Window
{
    private readonly VirtualMachineEntity? _existingVm;

    public AddVmWindow() => InitializeComponent();
    public AddVmWindow(ICredentialStore store, VirtualMachineEntity? existingVm = null) : this()
    {
        _store = store;
        _existingVm = existingVm;
        if (existingVm is not null)
        {
            Title = "Edit VM";
            SetWindowStateForEdit();
        }

        Loaded += async (_, _) => await ReloadCredentialsAsync();
    }

    private ICredentialStore? _store;
    public ObservableCollection<SavedCredential> Credentials { get; } = new();
    public string VmName => NameBox.Text;
    public string HostName => HostBox.Text;
    public string UserName => VmCredentialResolver.ResolveUsername(CredentialReference, SelectedCredentialUsername, UserBox.Text);
    public int PortNumber { get; private set; } = 3389;
    public string CredentialReference => (CredentialBox.SelectedItem as SavedCredential)?.Reference ?? string.Empty;
    public string SelectedCredentialUsername => (CredentialBox.SelectedItem as SavedCredential)?.Username ?? string.Empty;
    public bool LaunchSeparateWindow => SeparateWindowBox.IsChecked == true;
    public bool DriveRedirection => DriveRedirectBox.IsChecked == true;

    private void SetWindowStateForEdit()
    {
        if (_existingVm is null) return;

        NameBox.Text = _existingVm.Name;
        HostBox.Text = _existingVm.Host;
        UserBox.Text = _existingVm.Username;
        PortBox.Text = _existingVm.Port.ToString();
        SeparateWindowBox.IsChecked = string.Equals(_existingVm.PreferredSessionDisplayMode, SessionDisplayMode.SeparateWindow.ToString(), StringComparison.OrdinalIgnoreCase);
        DriveRedirectBox.IsChecked = _existingVm.DriveRedirection;
    }

    private async Task ReloadCredentialsAsync()
    {
        Credentials.Clear();
        if (_store is null) return;

        foreach (var credential in await _store.GetCredentialsAsync()) Credentials.Add(credential);
        CredentialBox.ItemsSource = Credentials;

        if (_existingVm is not null && !string.IsNullOrWhiteSpace(_existingVm.CredentialReference))
        {
            var matching = Credentials.FirstOrDefault(c => string.Equals(c.Reference, _existingVm.CredentialReference, StringComparison.OrdinalIgnoreCase));
            if (matching is not null)
            {
                CredentialBox.SelectedItem = matching;
                UserBox.Text = matching.Username;
                return;
            }
        }

        if (Credentials.Count == 0)
        {
            UserBox.Text = _existingVm?.Username ?? string.Empty;
            return;
        }

        CredentialBox.SelectedIndex = 0;
        UserBox.Text = _existingVm?.Username ?? Credentials[0].Username;
    }

    private async void ManageCredentialsClick(object sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        new CredentialManagerWindow(_store) { Owner = this }.ShowDialog();
        await ReloadCredentialsAsync();
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VmName) || string.IsNullOrWhiteSpace(HostName))
        {
            System.Windows.MessageBox.Show(this, "Name and host are required.", "Add VM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            System.Windows.MessageBox.Show(this, "Port must be between 1 and 65535.", "Add VM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var resolvedUsername = UserName;
        if (string.IsNullOrWhiteSpace(resolvedUsername) && string.IsNullOrWhiteSpace(CredentialReference))
        {
            System.Windows.MessageBox.Show(this, "Provide a username or choose a saved credential before adding the VM.", "Add VM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PortNumber = port;
        DialogResult = true;
    }
}