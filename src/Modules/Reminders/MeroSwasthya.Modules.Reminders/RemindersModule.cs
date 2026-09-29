using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Modules.Reminders.Endpoints;
using MeroSwasthya.Modules.Reminders.Infrastructure;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Reminders;

/// <summary>Reminders (A.2 Reminder, A.4 GET /patients/:id/reminders) and, later, their scheduling, delivery and the mock SMS outbox. Schema <c>reminders</c>.</summary>
public sealed class RemindersModule : IModule
{
    public string Name => "reminders";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<RemindersDbContext>(config, RemindersDbContext.Schema);
        services.AddScoped<ReminderService>();
        services.AddScoped<IPregnancyReminderSource, PregnancyReminderSource>();
        services.AddScoped<IModuleInitializer, RemindersModuleInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => ReminderEndpoints.Map(api);
}
