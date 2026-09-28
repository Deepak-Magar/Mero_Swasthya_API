using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeroSwasthya.Shared;

public static class SharedServices
{
    /// <summary>Envelope, JSON conventions, exception handling, clock, validation and signing keys.</summary>
    public static IServiceCollection AddSharedBuildingBlocks(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JsonOptions>(o => JsonDefaults.Apply(o.SerializerOptions));
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true); // binding errors → envelope
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<Events.IDomainEventPublisher, Events.InProcessDomainEventPublisher>();
        ValidationExtensions.ConfigureGlobal();

        var jwt = config.GetSection(JwtSettings.Section).Get<JwtSettings>() ?? new JwtSettings();
        services.AddSingleton(jwt);
        services.AddSingleton(new SigningKeys(jwt));

        services.AddSingleton(config.GetSection(FeatureFlags.Section).Get<FeatureFlags>() ?? new FeatureFlags());
        return services;
    }
}
