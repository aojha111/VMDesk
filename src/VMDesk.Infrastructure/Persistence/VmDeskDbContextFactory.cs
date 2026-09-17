using Microsoft.EntityFrameworkCore;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>
/// Creates short-lived <see cref="VmDeskDbContext"/> instances (spec §27, §68).
/// A factory is required so UI-thread and background work never share a context.
/// </summary>
public sealed class VmDeskDbContextFactory : IDbContextFactory<VmDeskDbContext>
{
    private readonly string _databasePath;

    public VmDeskDbContextFactory(string databasePath)
    {
        _databasePath = databasePath;
    }

    public VmDeskDbContext CreateDbContext() => new(_databasePath);

    public Task<VmDeskDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

/// <summary>Design-time factory used by "dotnet ef migrations" (spec §27).</summary>
public sealed class VmDeskDesignTimeDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<VmDeskDbContext>
{
    public VmDeskDbContext CreateDbContext(string[] args)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VMDesk",
            "vmdesk.db");
        var options = new DbContextOptionsBuilder<VmDeskDbContext>()
            .UseSqlite($"Data Source={path};Cache=Shared")
            .Options;
        return new VmDeskDbContext(options);
    }
}
