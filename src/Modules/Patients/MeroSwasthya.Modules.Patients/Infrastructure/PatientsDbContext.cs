using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Shared.Json;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Patients.Infrastructure;

/// <summary>Schema <c>patients</c>.</summary>
internal sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options) : DbContext(options)
{
    public const string Schema = "patients";

    public DbSet<Patient> Patients => Set<Patient>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Patient>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OwnerUserId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Sex).HasConversion(v => v.ToWire(), v => WireEnum.Parse<Sex>(v)).HasMaxLength(8);
            e.Property(x => x.BloodGroup).HasMaxLength(3);
            e.Property(x => x.Municipality).HasMaxLength(100);
            e.Property(x => x.EmergencyContactPhone).HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.UpdatedAt); // sync pull cursor
        });
    }
}
