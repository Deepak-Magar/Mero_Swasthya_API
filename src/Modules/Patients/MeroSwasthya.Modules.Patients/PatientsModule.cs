using FluentValidation;
using MeroSwasthya.Modules.Patients.Application;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Patients.Endpoints;
using MeroSwasthya.Modules.Patients.Infrastructure;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Patients;

/// <summary>
/// Patient profiles (A.4) and the owner of the cross-module read models: access checks, the summary
/// block and the timeline. Other modules extend them through the interfaces in Contracts/.
/// </summary>
public sealed class PatientsModule : IModule
{
    public string Name => "patients";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<PatientsDbContext>(config, PatientsDbContext.Schema);

        services.AddScoped<PatientAccessService>();
        services.AddScoped<IPatientAccess>(sp => sp.GetRequiredService<PatientAccessService>());
        services.AddScoped<IPatientDirectory, PatientDirectory>();
        services.AddScoped<IPatientSummaryService, PatientSummaryService>();
        services.AddScoped<IPatientTimelineService, PatientTimelineService>();
        services.AddScoped<PatientService>();
        services.AddScoped<IModuleInitializer, PatientsModuleInitializer>();
        services.AddValidatorsFromAssemblyContaining<PatientsModule>(includeInternalTypes: true);
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => PatientEndpoints.Map(api);
}
