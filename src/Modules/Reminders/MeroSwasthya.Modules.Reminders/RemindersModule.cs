using MeroSwasthya.Shared.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Reminders;

/// <summary>
/// Placeholder: Reminders and the mock SMS outbox — A.2 Reminder, A.4 GET /patients/:id/reminders + GET /demo/sms.
/// Registered so the solution structure is final; implemented in a later session (see docs/BACKEND_PROGRESS.md).
/// </summary>
public sealed class RemindersModule : IModule
{
    public string Name => "reminders";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        // TODO(Reminders): DbContext (schema "reminders"), services, validators, IModuleInitializer.
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        // TODO(Reminders): endpoints.
    }
}
