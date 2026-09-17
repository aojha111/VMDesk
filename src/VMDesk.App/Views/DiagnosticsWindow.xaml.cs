using System.Collections.ObjectModel;
using System.Windows;
using VMDesk.Core.Models;

namespace VMDesk.App.Views;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(DiagnosticsInfo info)
    {
        InitializeComponent();
        DataContext = new ObservableCollection<DiagnosticRow>
        {
            new("Application", info.AppVersion),
            new("Operating system", info.OsVersion),
            new(".NET runtime", info.RuntimeVersion),
            new("RDP ActiveX", info.RdpControlStatus),
            new("Database", info.DatabaseStatus),
            new("Database path", info.DatabasePath),
            new("Credential store", info.CredentialStoreStatus),
            new("Network adapters", info.NetworkAdapters),
            new("Logs", info.LogPath)
        };
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed record DiagnosticRow(string Key, string Value);
}