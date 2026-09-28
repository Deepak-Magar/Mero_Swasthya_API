using Microsoft.OpenApi.Models;

namespace MeroSwasthya.Api.Hosting;

public static class ApiDocumentation
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Mero Swasthya API",
                Version = HealthEndpoint.ContractVersion,
                Description = "Modular-monolith backend for the Mero Swasthya app. Contract: FRONTEND_SPEC.md Part A.",
            });
            o.CustomSchemaIds(t => t.FullName!.Replace('+', '.'));
            var bearer = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            o.AddSecurityDefinition("Bearer", bearer);
            o.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
        });
        return services;
    }

    /// <summary>Swagger UI at <c>/swagger</c>, the document at <c>/swagger/v1/swagger.json</c>.</summary>
    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(o => o.DocumentTitle = "Mero Swasthya API");
        return app;
    }
}
