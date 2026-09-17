using VMDesk.Application.Services;
using VMDesk.Core.Entities;
using Xunit;

namespace VMDesk.Application.Tests;

public sealed class VmFilterTests
{
    [Fact]
    public void Apply_requires_all_search_terms_across_display_fields()
    {
        var vms = new[]
        {
            new VirtualMachineEntity { Name = "Production Web", Host = "10.0.0.10", Username = "admin" },
            new VirtualMachineEntity { Name = "Production Database", Host = "10.0.0.11", Username = "db" }
        };

        var result = VmFilter.Apply(vms, "production 10.0.0.11", null, null, false, null);

        var vm = Assert.Single(result);
        Assert.Equal("Production Database", vm.Name);
    }

    [Fact]
    public void Apply_can_filter_favorites_and_status()
    {
        var favorite = new VirtualMachineEntity { Name = "Favorite", Favorite = true };
        var other = new VirtualMachineEntity { Name = "Other", Favorite = false };

        var result = VmFilter.Apply(
            new[] { favorite, other }, null, null, null, true,
            vm => vm.Favorite);

        var vm = Assert.Single(result);
        Assert.Same(favorite, vm);
    }

    [Fact]
    public void CloneForDuplicate_copies_connection_settings_without_identity()
    {
        var source = new VirtualMachineEntity
        {
            Id = Guid.NewGuid(),
            Name = "Dev",
            Host = "dev.example",
            Port = 3390,
            CredentialReference = "VMDesk/secret"
        };

        var clone = VmFilter.CloneForDuplicate(source);

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("Dev (copy)", clone.Name);
        Assert.Equal(source.Host, clone.Host);
        Assert.Equal(source.Port, clone.Port);
        Assert.Null(clone.GroupId);
        Assert.Empty(clone.CredentialReference);
    }
}