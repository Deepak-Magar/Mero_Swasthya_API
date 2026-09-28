namespace MeroSwasthya.Api.Hosting;

/// <summary>
/// Accepts the short environment variable names the original spec used (GRANT_SECRET, SMS_MODE, …)
/// in addition to the standard .NET <c>Section__Key</c> form. The .NET form wins when both are set.
/// </summary>
public static class LegacyEnvironment
{
    private static readonly (string Env, string Key)[] Map =
    [
        ("JWT_SECRET", "Jwt:AccessSigningKey"),
        ("GRANT_SECRET", "Jwt:GrantSigningKey"),
        ("SMS_MODE", "Features:SmsMode"),
        ("AI_MODE", "Features:AiMode"),
        ("OTP_DEMO", "Features:OtpDemo"),
    ];

    public static void Apply(ConfigurationManager config)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (env, key) in Map)
        {
            var value = Environment.GetEnvironmentVariable(env);
            var dotnetForm = Environment.GetEnvironmentVariable(key.Replace(":", "__"));
            if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(dotnetForm))
                values[key] = value;
        }

        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrEmpty(databaseUrl) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Default")))
            values["ConnectionStrings:Default"] = FromUrl(databaseUrl);

        if (values.Count > 0) config.AddInMemoryCollection(values);
    }

    /// <summary><c>postgres://user:pass@host:5432/db</c> → Npgsql keyword form.</summary>
    internal static string FromUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var port = uri.Port > 0 ? uri.Port : 5432;
        return $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};" +
               $"Username={Uri.UnescapeDataString(userInfo[0])};" +
               $"Password={(userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "")}";
    }
}
