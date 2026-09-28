using FluentValidation;
using MeroSwasthya.Modules.Clinical.Application;
using MeroSwasthya.Modules.Clinical.Endpoints;
using MeroSwasthya.Modules.Clinical.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Clinical;

/// <summary>Visits (+ prescriptions) and documents (MinIO / local fallback); summary + timeline contributions.</summary>
public sealed class ClinicalModule : IModule
{
    public string Name => "clinical";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<ClinicalDbContext>(config, ClinicalDbContext.Schema);

        services.AddSingleton(DocumentStorage.BindStorage(config));
        services.AddSingleton(DocumentStorage.BindS3(config));
        services.AddSingleton<S3DocumentStore>();
        services.AddSingleton<LocalDocumentStore>();
        services.AddSingleton<DocumentUrlSigner>();
        services.AddScoped<DocumentStorage>();
        services.AddScoped<DocumentMapper>();

        services.AddScoped<VisitService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<IPatientSummaryContributor, ClinicalSummaryContributor>();
        services.AddScoped<ITimelineContributor, ClinicalTimelineContributor>();
        services.AddScoped<IModuleInitializer, ClinicalModuleInitializer>();
        services.AddValidatorsFromAssemblyContaining<ClinicalModule>(includeInternalTypes: true);
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => ClinicalEndpoints.Map(api);
}
