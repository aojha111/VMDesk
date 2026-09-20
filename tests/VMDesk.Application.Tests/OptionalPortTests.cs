using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// The port is optional: leaving it empty stores 0 (= RDP default 3389) and
/// imports that carry no port must not invent one.
/// </summary>
public class OptionalPortTests
{
    private static (MainViewModel Model, List<VirtualMachineEntity> Added) CreateModel()
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
                default:
                    return Task.FromResult<object?>(null)!;
            }
        });
        var settings = ServiceStub.Create<ISettingsService>((_, _) =>
            Task.FromResult(LibraryViewMode.Tile));
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) => Task.FromResult<IReadOnlyList<VMDesk.Core.Models.SavedCredential>>(Array.Empty<VMDesk.Core.Models.SavedCredential>()));
        var model = new MainViewModel(new VmCatalogService(repository, credentials, LibraryReliabilityTests.Logs()), credentials, settings);
        return (model, added);
    }

    [Fact]
    public async Task AddAsync_without_a_port_stores_zero_as_the_default_marker()
    {
        var (model, added) = CreateModel();

        await model.AddAsync("No-port VM", "host1", port: null, "user", "", separateWindow: false, driveRedirection: false);

        var saved = Assert.Single(added);
        Assert.Equal(0, saved.Port);
        Assert.Equal("host1", saved.HostDisplay);
    }

    [Fact]
    public async Task AddAsync_with_a_custom_port_stores_it_and_shows_it_in_the_host()
    {
        var (model, added) = CreateModel();

        await model.AddAsync("Custom-port VM", "host2", port: 3390, "user", "", separateWindow: false, driveRedirection: false);

        var saved = Assert.Single(added);
        Assert.Equal(3390, saved.Port);
        Assert.Equal("host2:3390", saved.HostDisplay);
    }

    [Fact]
    public void Import_does_not_invent_a_default_port()
    {
        var target = new VirtualMachineEntity();
        var export = new VMDesk.Core.Models.ImportExportVm { Host = "imported-host", Port = 0 };

        ImportMapper.ApplyExport(target, export);

        Assert.Equal(0, target.Port);
        Assert.Equal("imported-host", target.HostDisplay);
    }
}
