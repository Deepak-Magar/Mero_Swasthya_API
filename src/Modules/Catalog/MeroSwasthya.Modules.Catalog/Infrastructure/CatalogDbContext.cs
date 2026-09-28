using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Shared.Json;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Catalog.Infrastructure;

/// <summary>Schema <c>catalog</c>: facilities, code lists, catalog settings.</summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<CodeListItem> CodeListItems => Set<CodeListItem>();
    public DbSet<CatalogSetting> Settings => Set<CatalogSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Facility>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion(v => v.ToWire(), v => WireEnum.Parse<FacilityType>(v)).HasMaxLength(32);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Municipality).HasMaxLength(100);
        });

        b.Entity<CodeListItem>(e =>
        {
            e.HasKey(x => new { x.Kind, x.Code });
            e.Property(x => x.Kind).HasConversion(v => v.ToWire(), v => WireEnum.Parse<CodeListKind>(v)).HasMaxLength(16);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.LabelEn).HasMaxLength(200);
            e.Property(x => x.LabelNp).HasMaxLength(200);
            e.Property(x => x.MetaJson).HasColumnName("meta").HasColumnType("jsonb");
            e.HasIndex(x => new { x.Kind, x.SortOrder });
        });

        b.Entity<CatalogSetting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Value).HasMaxLength(200);
        });
    }
}
