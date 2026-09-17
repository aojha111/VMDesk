using VMDesk.App.ViewModels;
using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;
using Xunit;

namespace VMDesk.Application.Tests;

public sealed class DeleteConfirmationTests
{
    private static MainViewModel CreateModel(bool confirm, Func<Task> delete, Func<Task<AppSettingsModel>>? getSettings = null)
    {
        var repository = ServiceStub.Create<IVmRepository>((method, _) => method.Name switch
        {
            nameof(IVmRepository.DeleteAsync) => delete(),
            nameof(IVmRepository.GetAllAsync) => Task.FromResult<IReadOnlyList<VirtualMachineEntity>>(Array.Empty<VirtualMachineEntity>()),
            _ => throw new NotSupportedException(method.Name)
        });
        var credentials = ServiceStub.Create<ICredentialStore>((_, _) =>
            throw new InvalidOperationException("Removal must not access credentials."));
        var settings = ServiceStub.Create<ISettingsService>((method, _) => method.Name switch
        {
            nameof(ISettingsService.GetAsync) => getSettings?.Invoke() ?? Task.FromResult(new AppSettingsModel { ConfirmBeforeDelete = confirm }),
            nameof(ISettingsService.GetValueAsync) => Task.FromResult(LibraryViewMode.Tile),
            _ => throw new NotSupportedException(method.Name)
        });
        return new MainViewModel(new VmCatalogService(repository, credentials, LibraryReliabilityTests.Logs()), credentials, settings);
    }

    [Theory]
    [InlineData(true, false, 1, 0)]
    [InlineData(true, true, 1, 1)]
    [InlineData(false, false, 0, 1)]
    public async Task Removal_honors_confirmation_setting(bool confirm, bool answer, int expectedPrompts, int expectedDeletes)
    {
        var prompts = 0;
        var deletes = 0;
        var model = CreateModel(confirm, () => { deletes++; return Task.CompletedTask; });
        var vm = new VirtualMachineEntity { Name = "Synthetic VM" };
        model.Vms.Add(vm);
        model.ConfirmDelete = item => { Assert.Same(vm, item); prompts++; return answer; };
        model.ActionFailed += (_, ex) => Assert.Fail(ex.ToString());

        await ((AsyncRelayCommand)model.DeleteVmCommand).ExecuteAsync(vm);

        Assert.Equal(expectedPrompts, prompts);
        Assert.Equal(expectedDeletes, deletes);
        Assert.Equal(expectedDeletes == 0 ? 1 : 0, model.Vms.Count);
    }

    [Fact]
    public async Task Missing_confirmation_handler_does_not_delete()
    {
        var deletes = 0;
        var model = CreateModel(true, () => { deletes++; return Task.CompletedTask; });
        await ((AsyncRelayCommand)model.DeleteVmCommand).ExecuteAsync(new VirtualMachineEntity());
        Assert.Equal(0, deletes);
    }

    [Fact]
    public async Task Pending_removal_blocks_reentry_and_reports_failure_then_allows_retry()
    {
        var pending = new TaskCompletionSource();
        var deletes = 0;
        var model = CreateModel(false, () => { deletes++; return deletes == 1 ? pending.Task : Task.CompletedTask; });
        var errors = new List<Exception>();
        model.ActionFailed += (_, ex) => errors.Add(ex);
        var vm = new VirtualMachineEntity();
        model.Vms.Add(vm);
        var command = (AsyncRelayCommand)model.DeleteVmCommand;
        var notifications = new List<bool>();
        command.CanExecuteChanged += (_, _) => notifications.Add(command.CanExecute(vm));

        var deleting = command.ExecuteAsync(vm);
        Assert.False(command.CanExecute(vm));
        await command.ExecuteAsync(vm);
        Assert.Equal(1, deletes);
        var failure = new InvalidOperationException("Synthetic storage failure");
        pending.SetException(failure);
        await deleting;

        Assert.Same(failure, Assert.Single(errors));
        Assert.Same(vm, Assert.Single(model.Vms));
        Assert.True(command.CanExecute(vm));
        Assert.Equal(new[] { false, true }, notifications);
        await command.ExecuteAsync(vm);
        Assert.Equal(2, deletes);
        Assert.Empty(model.Vms);
        Assert.True(command.CanExecute(vm));
    }

    [Fact]
    public async Task Settings_failure_is_reported_without_deleting()
    {
        var deletes = 0;
        var failure = new InvalidOperationException("Synthetic settings failure");
        var model = CreateModel(true, () => { deletes++; return Task.CompletedTask; },
            () => Task.FromException<AppSettingsModel>(failure));
        Exception? reported = null;
        model.ActionFailed += (_, ex) => reported = ex;
        var command = (AsyncRelayCommand)model.DeleteVmCommand;
        var vm = new VirtualMachineEntity();
        await command.ExecuteAsync(vm);
        Assert.Equal(0, deletes);
        Assert.Same(failure, reported);
        Assert.True(command.CanExecute(vm));
    }
}
