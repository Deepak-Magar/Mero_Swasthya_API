using MeroSwasthya.Modules.Auth.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Auth.Infrastructure;

/// <summary>Schema <c>auth</c>. Only the Auth module touches it.</summary>
internal sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public const string Schema = "auth";

    public DbSet<User> Users => Set<User>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Role).HasConversion(v => v.ToWire(), v => WireEnum.Parse<UserRole>(v)).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.FacilityId).HasMaxLength(64);
            e.Property(x => x.FacilityName).HasMaxLength(200);
            e.Property(x => x.PinHash).HasMaxLength(256);
        });

        b.Entity<OtpChallenge>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => new { x.Phone, x.CreatedAt });
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.ReplacedByHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.FamilyId);
        });

        b.Entity<InviteCode>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Role).HasConversion(v => v.ToWire(), v => WireEnum.Parse<UserRole>(v)).HasMaxLength(16);
            e.Property(x => x.FacilityId).HasMaxLength(64);
            e.Property(x => x.FacilityName).HasMaxLength(200);
        });
    }
}
