using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// The launch mode chosen in Add/Edit VM (checkbox "Launch this VM in a separate
/// window") must round-trip through persistence into the resolver's decision.
/// </summary>
public class VmLaunchModePersistenceTests
{
    private static (MainViewModel Model, IVmRepository Repository, List<VirtualMachineEntity> Added) CreateModel()
    {
        var added = new List<VirtualMachineEntity>();
        var repository = ServiceStub.Create<IVmRepository>((method, args) =>
        {
            switch (method.Name)
            {
                case nameof(IVmRepository.GetAllAsync):
                    return Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(added.ToList());
                case nameof(IVmRepository.AddAsync):
                    added.Add((VirtualMachineEntity)args![0]!);
                    return Task.FromResult((VirtualMachineEntity)args[0]!);
                case nameof(IVmRepository.UpdateAsync):
                    return Task.CompletedTask;
                default:
                    return Task.FromResult<object?>(null)!;
            }
        });
        var settings = ServiceStub.Create<ISettingsService>((_, _) =>
            Task.FromResult(LibraryViewMode.Tile));
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) => Task.FromResult<IReadOnlyList<VMDesk.Core.Models.SavedCredential>>(Array.Empty<VMDesk.Core.Models.SavedCredential>()));
        var model = new MainViewModel(new VmCatalogService(repository, credentials, LibraryReliabilityTests.Logs()), credentials, settings);
        return (model, repository, added);
    }

    [Fact]
    public async Task AddAsync_separate_window_choice_is_persisted_on_the_entity()
    {
        var (model, _, added) = CreateModel();
        await model.AddAsync("Standalone VM", "host1", 3389, "user", "VMDesk/cred", separateWindow: true, driveRedirection: false);

        var saved = Assert.Single(added);
        Assert.Equal(SessionDisplayMode.SeparateWindow.ToString(), saved.PreferredSessionDisplayMode);
        Assert.True(SessionLaunchResolver.IsSeparateWindow(saved));
    }

    [Fact]
    public async Task AddAsync_default_is_embedded()
    {
        var (model, _, added) = CreateModel();
        await model.AddAsync("Embedded VM", "host2", 3389, "user", "VMDesk/cred", separateWindow: false, driveRedirection: false);

        var saved = Assert.Single(added);
        Assert.True(SessionLaunchResolver.IsEmbedded(saved));
    }

    [Fact]
    public async Task UpdateAsync_can_switch_the_launch_mode_between_launches()
    {
        var (model, _, added) = CreateModel();
        await model.AddAsync("Switchable VM", "host3", 3389, "user", "VMDesk/cred", separateWindow: false, driveRedirection: false);
        var vm = added.Single();

        SessionLaunchResolver.Apply(vm, separateWindow: true);
        await model.UpdateAsync(vm);
        Assert.True(SessionLaunchResolver.IsSeparateWindow(vm));

        SessionLaunchResolver.Apply(vm, separateWindow: false);
        await model.UpdateAsync(vm);
        Assert.True(SessionLaunchResolver.IsEmbedded(vm));
    }
}
