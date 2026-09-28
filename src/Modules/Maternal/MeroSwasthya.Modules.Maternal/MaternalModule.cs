using MeroSwasthya.Shared.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Maternal;

/// <summary>
/// Placeholder: Pregnancies, ANC contacts, deliveries, triage, immunisations, growth — A.2, A.4 Maternal, A.5, A.6, addendum §1-2,5.
/// Registered so the solution structure is final; implemented in a later session (see docs/BACKEND_PROGRESS.md).
/// </summary>
public sealed class MaternalModule : IModule
{
    public string Name => "maternal";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        // TODO(Maternal): DbContext (schema "maternal"), services, validators, IModuleInitializer.
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        // TODO(Maternal): endpoints.
    }
}
