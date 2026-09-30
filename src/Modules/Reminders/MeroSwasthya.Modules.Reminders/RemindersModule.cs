using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Modules.Reminders.Endpoints;
using MeroSwasthya.Modules.Reminders.Infrastructure;
using MeroSwasthya.Shared;
using MeroSwasthya.Shared.Events;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using MeroSwasthya.Shared.Sms;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Reminders;

/// <summary>
/// Reminders (A.2 Reminder, A.4 GET /patients/:id/reminders), scheduled from Maternal's and Clinical's
/// domain events (A.5 <c>reminders</c>) and delivered by a background worker through <see cref="ISmsSender"/>
/// (the in-memory mock outbox when <c>Features:SmsMode = mock</c>). Schema <c>reminders</c>.
/// </summary>
public sealed class RemindersModule : IModule
{
    public string Name => "reminders";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<RemindersDbContext>(config, RemindersDbContext.Schema);
        services.AddScoped<ReminderService>();
        services.AddScoped<IPregnancyReminderSource, PregnancyReminderSource>();
        services.AddScoped<ReminderScheduler>();
        services.AddScoped<IDomainEventHandler<PregnancyRegistered>, PregnancyRegisteredHandler>();
        services.AddScoped<IDomainEventHandler<AncContactRecorded>, AncContactRecordedHandler>();
        services.AddScoped<IDomainEventHandler<PregnancyClosed>, PregnancyClosedHandler>();
        services.AddScoped<IDomainEventHandler<FollowUpScheduled>, FollowUpScheduledHandler>();
        services.AddScoped<IDomainEventHandler<VisitSuperseded>, VisitSupersededHandler>();
        services.AddScoped<IModuleInitializer, RemindersModuleInitializer>();

        var features = config.GetSection(FeatureFlags.Section).Get<FeatureFlags>() ?? new FeatureFlags();
        if (features.SmsIsMock)
        {
            services.AddSingleton<MockSmsSender>();
            services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<MockSmsSender>());
        }
        else
        {
            services.AddSingleton<ISmsSender, UnconfiguredSmsSender>();
        }

        var delivery = config.GetSection(ReminderDeliveryOptions.Section).Get<ReminderDeliveryOptions>() ?? new ReminderDeliveryOptions();
        services.AddSingleton(delivery);
        services.AddScoped<ReminderDispatcher>();
        if (delivery.WorkerEnabled) services.AddHostedService<ReminderDeliveryWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        ReminderEndpoints.Map(api);
        DevSmsEndpoints.Map(api);
    }
}
