using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Enums;

namespace VMDesk.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly VmCatalogService _catalog;
    private readonly ICredentialStore _credentials;
    private string _searchText = string.Empty;
    private bool _isBusy;
    private bool _isListView;
    private bool _viewLoaded;
    private string _scope = "All";
    private readonly ISettingsService _settings;

    public MainViewModel(VmCatalogService catalog, ICredentialStore credentials, ISettingsService settings)
    {
        _catalog = catalog;
        _credentials = credentials;
        _settings = settings;
        RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
        AddVmCommand = new RelayCommand(_ => AddVmRequested?.Invoke(this, EventArgs.Empty), _ => !IsBusy);
        DeleteVmCommand = new AsyncRelayCommand(async value =>
        {
            if (value is VirtualMachineEntity vm)
            {
                var settings = await _settings.GetAsync();
                if (settings.ConfirmBeforeDelete && ConfirmDelete?.Invoke(vm) != true) return;
                await _catalog.DeleteAsync(vm);
                await LoadAsync();
            }
        }, ex => ActionFailed?.Invoke(this, ex), value => value is VirtualMachineEntity && !IsBusy);
        FavoriteCommand = new RelayCommand(async value =>
        {
            if (value is VirtualMachineEntity vm)
            {
                await _catalog.SetFavoriteAsync(vm, !vm.Favorite);
                await LoadAsync();
            }
        }, value => value is VirtualMachineEntity && !IsBusy);
        ConnectCommand = new RelayCommand(value =>
        {
            if (value is VirtualMachineEntity vm) ConnectRequested?.Invoke(this, vm);
        }, value => value is VirtualMachineEntity && !IsBusy);
        ToggleViewCommand = new RelayCommand(async _ =>
        {
            IsListView = !IsListView;
            await _settings.SetValueAsync("library.view", IsListView ? LibraryViewMode.List : LibraryViewMode.Tile);
        });
    }

    public ObservableCollection<VirtualMachineEntity> Vms { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand AddVmCommand { get; }
    public ICommand DeleteVmCommand { get; }
    public ICommand FavoriteCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand ToggleViewCommand { get; }
    public Func<VirtualMachineEntity, bool>? ConfirmDelete { get; set; }
    public event EventHandler<Exception>? ActionFailed;
    public event EventHandler? AddVmRequested;
    public event EventHandler<VirtualMachineEntity>? ConnectRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            _ = LoadAsync();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddVmCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)FavoriteCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)DeleteVmCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsListView
    {
        get => _isListView;
        private set
        {
            if (_isListView == value) return;
            _isListView = value;
            OnPropertyChanged();
        }
    }

    public string Scope => _scope;

    public async Task SetScopeAsync(string scope)
    {
        _scope = scope;
        OnPropertyChanged(nameof(Scope));
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!_viewLoaded)
            {
                IsListView = await _settings.GetValueAsync("library.view", LibraryViewMode.Tile) == LibraryViewMode.List;
                _viewLoaded = true;
            }
            var all = await _catalog.GetAllAsync();
            var scoped = _scope switch
            {
                "Favorites" => all.Where(vm => vm.Favorite),
                "Recent" => all.Where(vm => vm.LastConnectedAt is not null),
                _ => all
            };
            var filtered = scoped.Where(vm => string.IsNullOrWhiteSpace(SearchText)
                || vm.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || vm.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || vm.Username.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            Vms.Clear();
            foreach (var vm in filtered) Vms.Add(vm);
            OnPropertyChanged(nameof(CountLabel));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string CountLabel => $"{Vms.Count} VM{(Vms.Count == 1 ? string.Empty : "s")}";

    public async Task AddAsync(string name, string host, int port, string username, string credentialReference, bool separateWindow, bool driveRedirection)
    {
        await _catalog.AddAsync(new VirtualMachineEntity
        {
            Name = name.Trim(),
            Host = host.Trim(),
            Port = port,
            Username = username.Trim(),
            CredentialReference = credentialReference,
            PreferredSessionDisplayMode = separateWindow ? SessionDisplayMode.SeparateWindow.ToString() : SessionDisplayMode.Embedded.ToString(),
            DriveRedirection = driveRedirection
        });
        await LoadAsync();
    }

    public async Task UpdateAsync(VirtualMachineEntity vm)
    {
        await _catalog.UpdateAsync(vm);
        await LoadAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}