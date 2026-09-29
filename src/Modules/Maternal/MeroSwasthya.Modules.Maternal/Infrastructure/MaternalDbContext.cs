using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Maternal.Infrastructure;

/// <summary>Schema <c>maternal</c>: pregnancies (birth plan as jsonb), ANC contacts (findings / referral as jsonb) and deliveries.</summary>
internal sealed class MaternalDbContext(DbContextOptions<MaternalDbContext> options) : DbContext(options)
{
    public const string Schema = "maternal";

    public DbSet<Pregnancy> Pregnancies => Set<Pregnancy>();
    public DbSet<AncContact> AncContacts => Set<AncContact>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Pregnancy>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PatientId).HasMaxLength(64);
            e.Property(x => x.RegisteredByUserId).HasMaxLength(64);
            e.Property(x => x.RiskLevel).HasConversion(v => v.ToWire(), v => WireEnum.Parse<RiskLevel>(v)).HasMaxLength(8);
            e.Property(x => x.Status).HasConversion(v => v.ToWire(), v => WireEnum.Parse<PregnancyStatus>(v)).HasMaxLength(16);
            e.Property(x => x.BirthPlan).AsJsonb();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => x.UpdatedAt);
        });

        b.Entity<AncContact>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PregnancyId).HasMaxLength(64);
            e.Property(x => x.PatientId).HasMaxLength(64);
            e.Property(x => x.ProviderUserId).HasMaxLength(64);
            e.Property(x => x.Findings).AsJsonb();
            e.Property(x => x.TriageLevel).HasConversion(v => v!.Value.ToWire(), v => WireEnum.Parse<TriageLevel>(v)).HasMaxLength(8);
            e.Property(x => x.Referral).AsJsonb();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.PregnancyId, x.ContactNo }).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.DoneAt });
            e.HasIndex(x => x.UpdatedAt);
        });
    }
}
