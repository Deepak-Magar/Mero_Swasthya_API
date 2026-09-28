using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Shared.Persistence;

/// <summary>
/// One DbContext per module, one PostgreSQL schema per DbContext, snake_case columns, and a
/// migrations history table inside the module's own schema. No module ever opens another
/// module's DbContext.
/// </summary>
public static class ModuleDb
{
    public const string ConnectionName = "Default";
    public const string HistoryTable = "__ef_migrations_history";

    public static string ConnectionString(IConfiguration config) =>
        config.GetConnectionString(ConnectionName)
        ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");

    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration config, string schema)
        where TContext : DbContext
    {
        var connectionString = ConnectionString(config);
        services.AddDbContext<TContext>(options => Configure(options, connectionString, schema, typeof(TContext)));
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString, string schema, Type contextType) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(HistoryTable, schema)
                .MigrationsAssembly(contextType.Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
}
