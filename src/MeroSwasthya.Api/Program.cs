using MeroSwasthya.Api.Hosting;
using MeroSwasthya.Shared;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Persistence;
using Serilog;

// `--migrate` applies every module's migrations and exits; `--seed` also seeds (idempotent) and exits.
var (hostArgs, cli) = CommandLineFlags.Parse(args);

var builder = WebApplication.CreateBuilder(hostArgs);
LegacyEnvironment.Apply(builder.Configuration);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

var modules = ModuleCatalog.All;

builder.Services.AddSharedBuildingBlocks(builder.Configuration);
foreach (var module in modules)
    module.AddModule(builder.Services, builder.Configuration);

builder.Services.AddHealthChecks()
    .AddNpgSql(ModuleDb.ConnectionString(builder.Configuration), name: "postgres", tags: ["db"]);
builder.Services.AddApiDocumentation();

var app = builder.Build();

app.UseExceptionHandler();
app.UseEnvelopeStatusCodes();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    app.UseApiDocumentation();

var api = app.MapGroup("/api/v1");
HealthEndpoint.Map(api, modules);
foreach (var module in modules)
    module.MapEndpoints(api);
if (app.Environment.IsDevelopment())
    DevEndpoints.Map(api);

await DatabaseBootstrap.RunAsync(app.Services, app.Configuration, cli, app.Logger);
if (cli.ExitAfterDatabase)
    return;

app.Run();

/// <summary>Exposed for WebApplicationFactory in the contract tests.</summary>
public partial class Program;
