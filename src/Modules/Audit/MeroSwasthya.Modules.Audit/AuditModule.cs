using MeroSwasthya.Modules.Audit.Application;
using MeroSwasthya.Modules.Audit.Contracts;
using MeroSwasthya.Modules.Audit.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Paging;
using MeroSwasthya.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Audit;

/// <summary>Who accessed a patient record (A.2 AuditEntry): the writer other modules call, and GET /patients/:id/audit.</summary>
public sealed class AuditModule : IModule
{
    public string Name => "audit";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<AuditDbContext>(config, AuditDbContext.Schema);
        services.AddScoped<AuditWriter>();
        services.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<AuditWriter>());
        services.AddScoped<IPatientReadObserver, RecordViewedObserver>();
        services.AddScoped<AuditQueries>();
        services.AddScoped<IModuleInitializer, AuditModuleInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        // A.4: owner only; newest first.
        api.MapGet("/patients/{id}/audit", async (
                string id, string? limit, IPatientAccess access, AuditQueries audit, CancellationToken ct) =>
            {
                var pageSize = Cursors.Limit(limit, 200, 500);
                await access.RequireOwnerAsync(id, ct);
                return ApiResults.Ok(new ItemsResponse<AuditEntryDto>(await audit.ForPatientAsync(id, pageSize, ct)));
            })
            .WithTags("Audit")
            .RequireAuthorization();
    }
}
