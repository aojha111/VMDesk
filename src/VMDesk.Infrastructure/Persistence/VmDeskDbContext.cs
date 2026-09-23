using Microsoft.EntityFrameworkCore;
using VMDesk.Core.Entities;

namespace VMDesk.Infrastructure.Persistence;

/// <summary>SQLite EF Core context (spec §27). WAL mode + busy timeout for robustness (spec §57).</summary>
public sealed class VmDeskDbContext : DbContext
{
    private readonly string _databasePath;

    public VmDeskDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    public VmDeskDbContext(DbContextOptions<VmDeskDbContext> options) : base(options)
    {
        _databasePath = string.Empty;
    }

    public DbSet<VirtualMachineEntity> VirtualMachines => Set<VirtualMachineEntity>();
    public DbSet<GroupEntity> Groups => Set<GroupEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<VmTagEntity> VmTags => Set<VmTagEntity>();
    public DbSet<ConnectionProfileEntity> ConnectionProfiles => Set<ConnectionProfileEntity>();
    public DbSet<ApplicationSettingEntity> ApplicationSettings => Set<ApplicationSettingEntity>();
    public DbSet<RecentConnectionEntity> RecentConnections => Set<RecentConnectionEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && !string.IsNullOrEmpty(_databasePath))
        {
            var dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            optionsBuilder.UseSqlite($"Data Source={_databasePath};Cache=Shared");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VirtualMachineEntity>(b =>
        {
            b.HasKey(v => v.Id);
            b.Property(v => v.Name).HasMaxLength(200).IsRequired();
            b.Property(v => v.Host).HasMaxLength(300).IsRequired();
            b.Property(v => v.CredentialReference).HasMaxLength(120);
            b.HasOne(v => v.Group).WithMany(g => g.VirtualMachines).HasForeignKey(v => v.GroupId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(v => v.Name);
            b.HasIndex(v => v.Host);
            b.HasIndex(v => v.Provider);
            // No unique index on (Provider, ProviderId): every Manual row has an empty ProviderId, so a unique
            // constraint would reject existing rows. DiscoverySyncService enforces provider identity by matching.
        });

        modelBuilder.Entity<GroupEntity>(b =>
        {
            b.HasKey(g => g.Id);
            b.Property(g => g.Name).HasMaxLength(120).IsRequired();
            b.HasIndex(g => g.Name).IsUnique();
        });

        modelBuilder.Entity<TagEntity>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).HasMaxLength(80).IsRequired();
            b.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<VmTagEntity>(b =>
        {
            b.HasKey(t => new { t.VmId, t.TagId });
            b.HasOne(t => t.Vm).WithMany(v => v.Tags).HasForeignKey(t => t.VmId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(t => t.Tag).WithMany(t => t.Vms).HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConnectionProfileEntity>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<ApplicationSettingEntity>(b =>
        {
            b.HasKey(s => s.Key);
            b.Property(s => s.Key).HasMaxLength(200);
        });

        modelBuilder.Entity<RecentConnectionEntity>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedOnAdd();
            b.HasIndex(r => r.ConnectedAt);
        });

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    public static void ConfigureSqlServeritePragmas(VmDeskDbContext context)
    {
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        context.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
    }
}
