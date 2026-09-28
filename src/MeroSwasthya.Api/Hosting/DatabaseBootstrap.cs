using MeroSwasthya.Shared.Modules;

namespace MeroSwasthya.Api.Hosting;

/// <summary>Runs module migrations, then module seeds, in <see cref="IModuleInitializer.Order"/>.</summary>
public static class DatabaseBootstrap
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration config, CommandLineFlags cli, ILogger logger)
    {
        var migrate = cli.Migrate || cli.Seed || config.GetValue<bool>("Database:MigrateOnStartup");
        var seed = cli.Seed || config.GetValue<bool>("Database:SeedOnStartup");
        if (migrate) await MigrateAsync(services, logger);
        if (seed) await SeedAsync(services, logger);
    }

    public static async Task MigrateAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var initializer in Ordered(scope.ServiceProvider))
        {
            logger.LogInformation("Migrating module {Module}", initializer.Module);
            await initializer.MigrateAsync(ct);
        }
    }

    public static async Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var initializer in Ordered(scope.ServiceProvider))
        {
            logger.LogInformation("Seeding module {Module}", initializer.Module);
            await initializer.SeedAsync(ct);
        }
    }

    private static IEnumerable<IModuleInitializer> Ordered(IServiceProvider sp) =>
        sp.GetServices<IModuleInitializer>().OrderBy(i => i.Order);
}
