using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using Xunit;

namespace VMDesk.Application.Tests;

public class SessionLaunchResolverTests
{
    [Fact]
    public void Separate_window_preference_resolves_to_standalone_launch()
    {
        var vm = new VirtualMachineEntity { PreferredSessionDisplayMode = "SeparateWindow" };
        Assert.True(SessionLaunchResolver.IsSeparateWindow(vm));
        Assert.False(SessionLaunchResolver.IsEmbedded(vm));
    }

    [Fact]
    public void Embedded_preference_and_defaults_resolve_to_embedded_launch()
    {
        Assert.True(SessionLaunchResolver.IsEmbedded(new VirtualMachineEntity()));
        Assert.True(SessionLaunchResolver.IsEmbedded(new VirtualMachineEntity { PreferredSessionDisplayMode = "Embedded" }));
        Assert.False(SessionLaunchResolver.IsSeparateWindow(new VirtualMachineEntity()));
    }

    [Theory]
    [InlineData("separatewindow")]
    [InlineData("SEPARATEWINDOW")]
    [InlineData("SeparateWindow ")]
    public void Comparison_is_case_insensitive_and_tolerant_of_whitespace(string stored)
    {
        var vm = new VirtualMachineEntity { PreferredSessionDisplayMode = stored };
        Assert.True(SessionLaunchResolver.IsSeparateWindow(vm));
    }

    [Theory]
    [InlineData("Garbage")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_empty_values_fall_back_to_embedded(string? stored)
    {
        var vm = new VirtualMachineEntity { PreferredSessionDisplayMode = stored! };
        Assert.True(SessionLaunchResolver.IsEmbedded(vm));
    }

    [Fact]
    public void Apply_persists_the_chosen_launch_mode()
    {
        var vm = new VirtualMachineEntity();
        SessionLaunchResolver.Apply(vm, separateWindow: true);
        Assert.Equal(SessionDisplayMode.SeparateWindow.ToString(), vm.PreferredSessionDisplayMode);

        SessionLaunchResolver.Apply(vm, separateWindow: false);
        Assert.Equal(SessionDisplayMode.Embedded.ToString(), vm.PreferredSessionDisplayMode);
    }
}
