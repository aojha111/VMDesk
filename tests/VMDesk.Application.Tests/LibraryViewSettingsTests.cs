using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// The tile/list choice saved in the Settings dialog must drive the library view,
/// and the toolbar toggle must still win as a runtime override.
/// </summary>
public class LibraryViewSettingsTests
{
    private sealed class FakeSettings : ISettingsService
    {
        private readonly Dictionary<string, object> _values = new();

        public AppSettingsModel Model { get; set; } = new();

        public bool HasValue(string key) => _values.ContainsKey(key);

        public Task<AppSettingsModel> GetAsync() => Task.FromResult(Model);

        public Task SaveAsync(AppSettingsModel settings)
        {
            Model = settings;
            return Task.CompletedTask;
        }

        public Task<T> GetValueAsync<T>(string key, T defaultValue) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : defaultValue);

        public Task SetValueAsync<T>(string key, T value)
        {
            _values[key] = value!;
            return Task.CompletedTask;
        }
    }

    private static MainViewModel CreateModel(FakeSettings settings)
    {
        var repository = ServiceStub.Create<IVmRepository>((method, args) =>
            method.Name switch
            {
                nameof(IVmRepository.GetAllAsync) => Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(Array.Empty<VirtualMachineEntity>()),
                _ => Task.FromResult<object?>(null)!
            });
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) => Task.FromResult<IReadOnlyList<SavedCredential>>(Array.Empty<SavedCredential>()));
        return new MainViewModel(new VmCatalogService(repository, credentials, LibraryReliabilityTests.Logs()), credentials, settings);
    }

    [Fact]
    public async Task DefaultView_saved_by_the_settings_dialog_drives_the_library_view()
    {
        var settings = new FakeSettings { Model = new AppSettingsModel { DefaultView = LibraryViewMode.List } };
        var model = CreateModel(settings);

        await model.LoadAsync();

        Assert.True(model.IsListView);
    }

    [Fact]
    public async Task DefaultView_tile_keeps_the_library_in_tile_view()
    {
        var settings = new FakeSettings { Model = new AppSettingsModel { DefaultView = LibraryViewMode.Tile } };
        var model = CreateModel(settings);

        await model.LoadAsync();

        Assert.False(model.IsListView);
    }

    [Fact]
    public async Task Toolbar_toggle_overrides_the_saved_default_for_the_next_session()
    {
        var settings = new FakeSettings { Model = new AppSettingsModel { DefaultView = LibraryViewMode.Tile } };
        var model = CreateModel(settings);
        await model.LoadAsync();
        Assert.False(model.IsListView);

        model.ToggleViewCommand.Execute(null);
        for (var attempt = 0; attempt < 40 && !settings.HasValue("library.view"); attempt++)
            await Task.Delay(25);
        Assert.True(model.IsListView);

        var reloaded = CreateModel(settings);
        await reloaded.LoadAsync();
        Assert.True(reloaded.IsListView);
    }

    [Fact]
    public async Task ApplySavedLibraryViewAsync_picks_up_the_dialog_choice_without_a_restart()
    {
        var settings = new FakeSettings();
        var model = CreateModel(settings);
        await model.LoadAsync();
        Assert.False(model.IsListView);

        // What the Settings dialog's Apply does: persist the choice in both stores.
        settings.Model = new AppSettingsModel { DefaultView = LibraryViewMode.List };
        await settings.SetValueAsync("library.view", LibraryViewMode.List);

        await model.ApplySavedLibraryViewAsync();

        Assert.True(model.IsListView);
    }
}
