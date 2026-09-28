using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MeroSwasthya.ContractTests.Infrastructure;

/// <summary>
/// Boots the real host (all modules, real HTTP pipeline) against a real PostgreSQL:
/// <list type="bullet">
/// <item>Docker available → a Testcontainers <c>postgres:16</c> container.</item>
/// <item>Otherwise → a throwaway database on the server in <c>MS_TEST_PG</c>
/// (default <c>127.0.0.1:5433 swc/swc</c>, the server <c>scripts/dev.ps1</c> starts), dropped afterwards.</item>
/// </list>
/// Migrations and the idempotent seed run at startup, exactly as in Development.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DefaultLocalServer = "Host=127.0.0.1;Port=5433;Username=swc;Password=swc;Database=postgres";

    private PostgreSqlContainer? _container;
    private string? _localAdminConnection;
    private string? _localDatabase;
    private string _connectionString = "";

    /// <summary>Human-readable description of where the tests ran; printed by <c>DatabaseModeTests</c>.</summary>
    public string DatabaseMode { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (DockerProbe.IsAvailable())
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("swc")
                .WithUsername("swc")
                .WithPassword("swc")
                .Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
            DatabaseMode = "Testcontainers (postgres:16 in Docker)";
        }
        else
        {
            _localAdminConnection = Environment.GetEnvironmentVariable("MS_TEST_PG") ?? DefaultLocalServer;
            _localDatabase = $"swc_test_{Guid.NewGuid():N}"[..20];
            await using (var admin = new NpgsqlConnection(_localAdminConnection))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_localDatabase}\"", admin);
                await create.ExecuteNonQueryAsync();
                DatabaseMode = $"Local PostgreSQL {admin.PostgreSqlVersion} at {admin.Host}:{admin.Port} " +
                               $"(Docker unavailable), throwaway database {_localDatabase}";
            }
            _connectionString = new NpgsqlConnectionStringBuilder(_localAdminConnection) { Database = _localDatabase }
                .ConnectionString;
        }

        _ = Server; // build the host now: migrations + seed run once for the whole collection
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Jwt:AccessSigningKey", "test-access-signing-key-0123456789abcdef");
        builder.UseSetting("Jwt:GrantSigningKey", "test-grant-signing-key-fedcba9876543210");
        builder.UseSetting("Features:SmsMode", "mock");
        builder.UseSetting("Features:OtpDemo", "true");
        builder.UseSetting("Features:AiMode", "off");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedOnStartup", "true");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        if (_localAdminConnection is not null && _localDatabase is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_localAdminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_localDatabase}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

internal static class DockerProbe
{
    /// <summary>Docker counts as available when DOCKER_HOST is set or a docker CLI is on PATH and answers.</summary>
    public static bool IsAvailable()
    {
        if (Environment.GetEnvironmentVariable("MS_TEST_NO_DOCKER") == "1") return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))) return true;

        var exe = OperatingSystem.IsWindows() ? "docker.exe" : "docker";
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, exe)));
        if (!onPath) return false;

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return false;
            if (!process.WaitForExit(15_000)) { process.Kill(); return false; }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
