using System.Reflection;
using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Interfaces;
using Xunit;

namespace VMDesk.Application.Tests;

public class ServiceStub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);

    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, ServiceStub>();
        ((ServiceStub)(object)proxy).Handler = handler;
        return proxy;
    }
}

public sealed class LibraryReliabilityTests
{
    internal static IAppLogFactory Logs() => ServiceStub.Create<IAppLogFactory>((_, _) =>
        ServiceStub.Create<IAppLog>((_, _) => null));

    [Fact]
    public async Task Connect_is_disabled_during_load_and_enabled_after_load()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<VirtualMachineEntity>>();
        var vm = new VirtualMachineEntity { Name = "Connect regression VM" };
        var repository = ServiceStub.Create<IVmRepository>((_, _) => pending.Task);
        var settings = ServiceStub.Create<ISettingsService>((method, args) => method.Name switch
        {
            nameof(ISettingsService.GetAsync) => Task.FromResult(new VMDesk.Core.Models.AppSettingsModel()),
            nameof(ISettingsService.GetValueAsync) => Task.FromResult((VMDesk.Core.Enums.LibraryViewMode)args![1]!),
            _ => Task.CompletedTask
        });
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) =>
            throw new InvalidOperationException("Credentials must not be accessed."));
        var model = new MainViewModel(new VmCatalogService(repository, credentials, Logs()), credentials, settings);
        var enabledNotifications = new List<bool>();
        model.ConnectCommand.CanExecuteChanged += (_, _) =>
            enabledNotifications.Add(model.ConnectCommand.CanExecute(vm));

        var loading = model.LoadAsync();
        Assert.False(loading.IsCompleted);
        Assert.True(model.IsBusy);
        Assert.False(model.ConnectCommand.CanExecute(vm));
        Assert.False(model.ConnectCommand.CanExecute(null));

        pending.SetResult(new[] { vm });
        await loading;

        Assert.False(model.IsBusy);
        Assert.Same(vm, Assert.Single(model.Vms));
        Assert.True(model.ConnectCommand.CanExecute(vm));
        Assert.False(model.ConnectCommand.CanExecute(null));
        Assert.Equal(new[] { false, true }, enabledNotifications);

        VirtualMachineEntity? requested = null;
        model.ConnectRequested += (_, item) => requested = item;
        model.ConnectCommand.Execute(vm);
        Assert.Same(vm, requested);
    }

    [Fact]
    public async Task Loading_notifies_all_row_commands_when_they_become_enabled()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<VirtualMachineEntity>>();
        var vm = new VirtualMachineEntity { Name = "Test VM" };
        var repository = ServiceStub.Create<IVmRepository>((_, _) => pending.Task);
        var settings = ServiceStub.Create<ISettingsService>((method, args) => method.Name switch
        {
            nameof(ISettingsService.GetAsync) => Task.FromResult(new VMDesk.Core.Models.AppSettingsModel()),
            nameof(ISettingsService.GetValueAsync) => Task.FromResult((VMDesk.Core.Enums.LibraryViewMode)args![1]!),
            _ => Task.CompletedTask
        });
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) => throw new InvalidOperationException("Credentials must not be accessed."));
        var model = new MainViewModel(new VmCatalogService(repository, credentials, Logs()), credentials, settings);
        var commands = new[] { model.ConnectCommand, model.FavoriteCommand, model.DeleteVmCommand };
        var notifications = new int[commands.Length];
        for (var i = 0; i < commands.Length; i++)
        {
            var index = i;
            commands[i].CanExecuteChanged += (_, _) => notifications[index]++;
        }

        var loading = model.LoadAsync();
        Assert.All(commands, command => Assert.False(command.CanExecute(vm)));
        pending.SetResult(new[] { vm });
        await loading;

        Assert.All(commands, command => Assert.True(command.CanExecute(vm)));
        Assert.All(notifications, count => Assert.Equal(2, count));
        VirtualMachineEntity? requested = null;
        model.ConnectRequested += (_, item) => requested = item;
        model.ConnectCommand.Execute(vm);
        Assert.Same(vm, requested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_a_vm_never_deletes_saved_credentials(bool repositoryFails)
    {
        var credentialCalls = 0;
        var deleted = Guid.Empty;
        var vm = new VirtualMachineEntity { Id = Guid.NewGuid(), CredentialReference = "VMDesk/shared" };
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) => { credentialCalls++; return Task.CompletedTask; });
        var repository = ServiceStub.Create<IVmRepository>((method, args) =>
        {
            Assert.Equal(nameof(IVmRepository.DeleteAsync), method.Name);
            deleted = (Guid)args![0]!;
            return repositoryFails ? Task.FromException(new InvalidOperationException("Storage unavailable")) : Task.CompletedTask;
        });
        var catalog = new VmCatalogService(repository, credentials, Logs());

        if (repositoryFails) await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.DeleteAsync(vm));
        else await catalog.DeleteAsync(vm);

        Assert.Equal(vm.Id, deleted);
        Assert.Equal(0, credentialCalls);
    }
}
