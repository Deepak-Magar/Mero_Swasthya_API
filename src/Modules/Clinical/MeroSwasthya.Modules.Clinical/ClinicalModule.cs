using MeroSwasthya.Shared.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Clinical;

/// <summary>
/// Placeholder: Visits, prescriptions and documents — A.2 Visit/Prescription/Document, A.4 Visits + Documents.
/// Registered so the solution structure is final; implemented in a later session (see docs/BACKEND_PROGRESS.md).
/// </summary>
public sealed class ClinicalModule : IModule
{
    public string Name => "clinical";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        // TODO(Clinical): DbContext (schema "clinical"), services, validators, IModuleInitializer.
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        // TODO(Clinical): endpoints.
    }
}
