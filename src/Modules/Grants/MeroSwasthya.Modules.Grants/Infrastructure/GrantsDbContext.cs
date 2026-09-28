using MeroSwasthya.Modules.Grants.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Grants.Infrastructure;

/// <summary>Schema <c>grants</c>.</summary>
internal sealed class GrantsDbContext(DbContextOptions<GrantsDbContext> options) : DbContext(options)
{
    public const string Schema = "grants";

    public DbSet<AccessGrant> Grants => Set<AccessGrant>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<AccessGrant>(e =>
        {
            e.ToTable("access_grants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PatientId).HasMaxLength(64);
            e.Property(x => x.Scope).HasConversion(v => v.ToWire(), v => WireEnum.Parse<GrantScope>(v)).HasMaxLength(8);
            e.Property(x => x.Jti).HasMaxLength(64);
            e.Property(x => x.CreatedByUserId).HasMaxLength(64);
            e.Property(x => x.RedeemedByUserId).HasMaxLength(64);
            e.HasIndex(x => x.Jti).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.CreatedAt });
            e.HasIndex(x => new { x.RedeemedByUserId, x.PatientId });
        });
    }
}

internal sealed class GrantsModuleInitializer(GrantsDbContext db) : IModuleInitializer
{
    public string Module => "grants";
    public int Order => 35;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    // TODO(seed): the three dashboard women p_…0003–0005 with grants already redeemed by the seeded
    // provider (addendum §7) — needs their pregnancies, so it lands with the Maternal module.
    public Task SeedAsync(CancellationToken ct) => Task.CompletedTask;
}
