using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Maternal.Infrastructure;

/// <summary>Schema <c>maternal</c>: pregnancies, ANC contacts and deliveries.</summary>
internal sealed class MaternalDbContext(DbContextOptions<MaternalDbContext> options) : DbContext(options)
{
    public const string Schema = "maternal";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
    }
}
