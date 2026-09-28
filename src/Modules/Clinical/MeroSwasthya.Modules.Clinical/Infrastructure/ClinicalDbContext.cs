using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Clinical.Infrastructure;

/// <summary>Schema <c>clinical</c>: visits (vitals, referral, prescriptions as jsonb) and document metadata.</summary>
internal sealed class ClinicalDbContext(DbContextOptions<ClinicalDbContext> options) : DbContext(options)
{
    public const string Schema = "clinical";

    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Visit>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PatientId).HasMaxLength(64);
            e.Property(x => x.ProviderUserId).HasMaxLength(64);
            e.Property(x => x.ProviderName).HasMaxLength(100);
            e.Property(x => x.FacilityId).HasMaxLength(64);
            e.Property(x => x.FacilityName).HasMaxLength(200);
            e.Property(x => x.ChiefComplaintCode).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Advice).HasMaxLength(1000);
            e.Property(x => x.SupersedesId).HasMaxLength(64);
            e.Property(x => x.Vitals).AsJsonb();
            e.Property(x => x.Referral).AsJsonb();
            e.Property(x => x.Prescriptions).AsJsonb();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.PatientId, x.VisitAt });
            e.HasIndex(x => x.UpdatedAt);
        });

        b.Entity<Document>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PatientId).HasMaxLength(64);
            e.Property(x => x.UploadedByUserId).HasMaxLength(64);
            e.Property(x => x.Type).HasConversion(v => v.ToWire(), v => WireEnum.Parse<DocumentType>(v)).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion(v => v.ToWire(), v => WireEnum.Parse<DocumentStatus>(v)).HasMaxLength(16);
            e.Property(x => x.ContentType).HasMaxLength(32);
            e.Property(x => x.ObjectKey).HasMaxLength(200);
            e.Property(x => x.Storage).HasMaxLength(8);
            e.Property(x => x.AiSummaryStatus).HasConversion(v => v.ToWire(), v => WireEnum.Parse<AiSummaryStatus>(v)).HasMaxLength(8);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.PatientId, x.TakenAt });
            e.HasIndex(x => x.UpdatedAt);
        });
    }
}
