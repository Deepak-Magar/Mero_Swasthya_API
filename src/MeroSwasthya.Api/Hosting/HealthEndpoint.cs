using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Time;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MeroSwasthya.Api.Hosting;

public static class HealthEndpoint
{
    public const string ContractVersion = "2026-09-18.1";

    public sealed record HealthData(
        string Status,
        string Service,
        string ContractVersion,
        DateTime ServerTime,
        string Database,
        IReadOnlyList<string> Modules);

    /// <summary>GET /api/v1/health — unauthenticated liveness + database check, in the envelope.</summary>
    public static void Map(IEndpointRouteBuilder api, IReadOnlyList<IModule> modules)
    {
        api.MapGet("/health", async (HealthCheckService health, IClock clock, CancellationToken ct) =>
            {
                var report = await health.CheckHealthAsync(ct);
                var database = report.Entries.TryGetValue("postgres", out var db) && db.Status == HealthStatus.Healthy
                    ? "up"
                    : "down";
                if (database == "down")
                    throw new AppException(ErrorCode.Internal, "Database unreachable", new { database });

                return ApiResults.Ok(new HealthData(
                    "healthy", "mero-swasthya-api", ContractVersion, clock.UtcNow, database,
                    modules.Select(m => m.Name).ToList()));
            })
            .WithTags("Health")
            .AllowAnonymous();
    }
}
