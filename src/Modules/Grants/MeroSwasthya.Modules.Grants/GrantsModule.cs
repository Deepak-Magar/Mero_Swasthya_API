using FluentValidation;
using MeroSwasthya.Modules.Grants.Application;
using MeroSwasthya.Modules.Grants.Contracts;
using MeroSwasthya.Modules.Grants.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using MeroSwasthya.Shared.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Grants;

/// <summary>Access grants (QR share codes): create, redeem (bundle), revoke; the grant side of patient access.</summary>
public sealed class GrantsModule : IModule
{
    public string Name => "grants";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<GrantsDbContext>(config, GrantsDbContext.Schema);
        services.AddSingleton<GrantTokens>();
        services.AddScoped<GrantService>();
        services.AddScoped<IPatientGrantSource, PatientGrantSource>();
        services.AddScoped<IGrantAuthorization, GrantAuthorization>();
        services.AddScoped<IModuleInitializer, GrantsModuleInitializer>();
        services.AddValidatorsFromAssemblyContaining<GrantsModule>(includeInternalTypes: true);
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        var grants = api.MapGroup("/grants").WithTags("Grants").RequireAuthorization();

        grants.MapPost("", async (CreateGrantRequest body, GrantService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.CreateAsync(body, ct)))
            .Validate<CreateGrantRequest>();

        grants.MapPost("/redeem", async (RedeemGrantRequest body, GrantService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.RedeemAsync(body, ct)))
            .Validate<RedeemGrantRequest>();

        grants.MapPost("/{id}/revoke", async (string id, GrantService svc, CancellationToken ct) =>
            ApiResults.Ok(new GrantResponse(await svc.RevokeAsync(id, ct))));
    }
}
