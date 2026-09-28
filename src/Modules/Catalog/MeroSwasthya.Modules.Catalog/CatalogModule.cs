using MeroSwasthya.Modules.Catalog.Application;
using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Endpoints;
using MeroSwasthya.Modules.Catalog.Infrastructure;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Catalog;

/// <summary>Facilities (+ nearby), code lists with version, /rules verbatim, /config flags.</summary>
public sealed class CatalogModule : IModule
{
    public string Name => "catalog";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<CatalogDbContext>(config, CatalogDbContext.Schema);
        services.AddSingleton<IRulesProvider, RulesProvider>();
        services.AddScoped<ICodeListLookup, CodeListLookup>();
        services.AddScoped<IFacilityDirectory, FacilityDirectory>();
        services.AddScoped<IModuleInitializer, CatalogModuleInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => CatalogEndpoints.Map(api);
}
