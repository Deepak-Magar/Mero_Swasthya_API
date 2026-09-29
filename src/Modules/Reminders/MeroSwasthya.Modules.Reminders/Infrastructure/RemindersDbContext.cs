using MeroSwasthya.Modules.Reminders.Domain;
using MeroSwasthya.Shared.Json;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Reminders.Infrastructure;

/// <summary>Schema <c>reminders</c>: the scheduled messages (server-owned, pull-only for the app).</summary>
internal sealed class RemindersDbContext(DbContextOptions<RemindersDbContext> options) : DbContext(options)
{
    public const string Schema = "reminders";

    public DbSet<Reminder> Reminders => Set<Reminder>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Reminder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PatientId).HasMaxLength(64);
            e.Property(x => x.PregnancyId).HasMaxLength(64);
            e.Property(x => x.Kind).HasConversion(v => v.ToWire(), v => WireEnum.Parse<ReminderKind>(v)).HasMaxLength(16);
            e.Property(x => x.Channel).HasConversion(v => v.ToWire(), v => WireEnum.Parse<ReminderChannel>(v)).HasMaxLength(8);
            e.Property(x => x.RecipientPhone).HasMaxLength(20);
            e.Property(x => x.RecipientRole).HasConversion(v => v.ToWire(), v => WireEnum.Parse<RecipientRole>(v)).HasMaxLength(8);
            e.Property(x => x.MessageNp).HasMaxLength(500);
            e.Property(x => x.MessageEn).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion(v => v.ToWire(), v => WireEnum.Parse<ReminderStatus>(v)).HasMaxLength(8);
            e.Property(x => x.SourceKey).HasMaxLength(120);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => new { x.SourceKey, x.RecipientPhone }).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.DueAt });
            e.HasIndex(x => x.PregnancyId);
            e.HasIndex(x => new { x.Status, x.DueAt });
        });
    }
}

internal sealed class RemindersModuleInitializer(RemindersDbContext db) : Shared.Modules.IModuleInitializer
{
    public string Module => "reminders";

    /// <summary>After Maternal (50): the seeded reminders come from the seeded pregnancy.</summary>
    public int Order => 60;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task SeedAsync(CancellationToken ct) => Task.CompletedTask;
}
