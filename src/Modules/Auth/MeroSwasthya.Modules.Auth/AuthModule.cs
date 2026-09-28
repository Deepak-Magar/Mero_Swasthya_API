using FluentValidation;
using MeroSwasthya.Modules.Auth.Application;
using MeroSwasthya.Modules.Auth.Contracts;
using MeroSwasthya.Modules.Auth.Endpoints;
using MeroSwasthya.Modules.Auth.Infrastructure;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Persistence;
using MeroSwasthya.Shared.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Modules.Auth;

/// <summary>OTP, PIN (Argon2id), access/refresh tokens, invite activation, /me, and <see cref="ICurrentUser"/>.</summary>
public sealed class AuthModule : IModule
{
    public string Name => "auth";

    public void AddModule(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDbContext<AuthDbContext>(config, AuthDbContext.Schema);

        var jwt = config.GetSection(JwtSettings.Section).Get<JwtSettings>() ?? new JwtSettings();
        services.AddAuthSchemes(jwt, new SigningKeys(jwt));

        services.AddSingleton<PinHasher>();
        services.AddSingleton<TokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ICurrentUser, CurrentUserAccessor>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IModuleInitializer, AuthModuleInitializer>();
        services.AddValidatorsFromAssemblyContaining<AuthModule>(includeInternalTypes: true);
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => AuthEndpoints.Map(api);
}
