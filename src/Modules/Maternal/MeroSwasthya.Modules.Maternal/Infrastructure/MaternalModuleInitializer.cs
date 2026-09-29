using MeroSwasthya.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Maternal.Infrastructure;

/// <summary>Migrates schema <c>maternal</c>. The demo seed (Sita's week-30 pregnancy) lands with the contract tests.</summary>
internal sealed class MaternalModuleInitializer(MaternalDbContext db) : IModuleInitializer
{
    public string Module => "maternal";

    /// <summary>After Patients (30) and Clinical (40): the seed refers to seeded patients.</summary>
    public int Order => 50;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task SeedAsync(CancellationToken ct) => Task.CompletedTask;
}
