using MeroSwasthya.Shared.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Sync;

/// <summary>
/// Placeholder: Offline sync push/pull — A.4 Sync, A.8, addendum §3.
/// Registered so the solution structure is final; implemented in a later session (see docs/BACKEND_PROGRESS.md).
/// </summary>
public sealed class SyncModule : IModule
{
    public string Name => "sync";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        // TODO(Sync): DbContext (schema "sync"), services, validators, IModuleInitializer.
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        // TODO(Sync): endpoints.
    }
}
