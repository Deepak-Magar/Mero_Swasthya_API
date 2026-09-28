using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Shared.Modules;

/// <summary>
/// A vertical slice of the monolith. The host calls <see cref="AddModule"/> for every module during
/// service registration and <see cref="MapEndpoints"/> with the <c>/api/v1</c> route group.
/// </summary>
public interface IModule
{
    string Name { get; }

    void AddModule(IServiceCollection services, IConfiguration config);

    void MapEndpoints(IEndpointRouteBuilder api);
}

/// <summary>
/// Registered (scoped) by modules that own a schema. The host runs every initializer's
/// <see cref="MigrateAsync"/> and then every <see cref="SeedAsync"/>, both ordered by <see cref="Order"/>.
/// Seeding must be idempotent.
/// </summary>
public interface IModuleInitializer
{
    string Module { get; }

    /// <summary>Lower runs first. Catalog (10) → Auth (20) → Patients (30) → later modules.</summary>
    int Order { get; }

    Task MigrateAsync(CancellationToken ct);

    Task SeedAsync(CancellationToken ct);
}
