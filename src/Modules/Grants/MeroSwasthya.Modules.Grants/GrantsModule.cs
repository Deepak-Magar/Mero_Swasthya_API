using MeroSwasthya.Shared.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Grants;

/// <summary>
/// Placeholder: Access grants (QR), redeem bundle, audit trail — A.2 AccessGrant/AuditEntry, A.4 Access grants + GET /patients/:id/audit, A.7, addendum §4.
/// Registered so the solution structure is final; implemented in a later session (see docs/BACKEND_PROGRESS.md).
/// </summary>
public sealed class GrantsModule : IModule
{
    public string Name => "grants";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        // TODO(Grants): DbContext (schema "grants"), services, validators, IModuleInitializer.
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        // TODO(Grants): endpoints.
    }
}
