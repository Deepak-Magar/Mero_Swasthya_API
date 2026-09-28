using MeroSwasthya.Modules.Audit.Contracts;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Audit.Infrastructure;

/// <summary>Append-only access log row. Actor name/facility are denormalised at write time (A.2).</summary>
internal sealed class AuditEntry
{
    public required string Id { get; init; }
    public required string PatientId { get; init; }
    public required string ActorUserId { get; init; }
    public required string ActorName { get; init; }
    public string? ActorFacilityName { get; init; }
    public AuditAction Action { get; init; }
    public DateTime At { get; init; }

    /// <summary>Insertion order — breaks ties between entries written in the same millisecond.</summary>
    public long Seq { get; init; }
}

/// <summary>Schema <c>audit</c>.</summary>
internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PatientId).HasMaxLength(64);
            e.Property(x => x.ActorUserId).HasMaxLength(64);
            e.Property(x => x.ActorName).HasMaxLength(100);
            e.Property(x => x.ActorFacilityName).HasMaxLength(200);
            e.Property(x => x.Action).HasConversion(v => v.ToWire(), v => WireEnum.Parse<AuditAction>(v)).HasMaxLength(32);
            e.Property(x => x.Seq).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.PatientId, x.At });
            e.HasIndex(x => new { x.ActorUserId, x.PatientId, x.Action, x.At });
        });
    }
}

internal sealed class AuditModuleInitializer(AuditDbContext db) : IModuleInitializer
{
    public string Module => "audit";
    public int Order => 15;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task SeedAsync(CancellationToken ct) => Task.CompletedTask; // entries come from actions, never from seed
}
