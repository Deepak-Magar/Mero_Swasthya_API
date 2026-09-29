using FluentValidation;
using MeroSwasthya.Modules.Maternal.Infrastructure;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Maternal;

/// <summary>
/// Pregnancies, ANC contacts with the A.5 danger-sign triage, and deliveries — A.2, A.4 "Maternal",
/// A.5, A.6 #1–#12, addendum §5. Schema <c>maternal</c>.
/// </summary>
public sealed class MaternalModule : IModule
{
    public string Name => "maternal";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<MaternalDbContext>(config, MaternalDbContext.Schema);
        services.AddScoped<IModuleInitializer, MaternalModuleInitializer>();
        services.AddValidatorsFromAssemblyContaining<MaternalModule>(includeInternalTypes: true);
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        // Endpoints are added feature by feature (pregnancies, contacts, delivery).
    }
}
