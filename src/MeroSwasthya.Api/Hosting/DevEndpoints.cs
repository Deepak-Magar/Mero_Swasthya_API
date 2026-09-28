using MeroSwasthya.Shared.Api;

namespace MeroSwasthya.Api.Hosting;

/// <summary>Development-only helpers. Never mapped outside the Development environment.</summary>
public static class DevEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        // POST /api/v1/dev/seed — re-run every module's idempotent seed without restarting.
        api.MapPost("/dev/seed", async (IServiceProvider services, ILoggerFactory loggers, CancellationToken ct) =>
            {
                await DatabaseBootstrap.SeedAsync(services, loggers.CreateLogger("DevSeed"), ct);
                return ApiResults.Ok(new { seeded = true });
            })
            .WithTags("Dev")
            .AllowAnonymous();
    }
}
