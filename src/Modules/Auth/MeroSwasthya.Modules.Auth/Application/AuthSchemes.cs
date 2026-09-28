using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeroSwasthya.Modules.Auth.Application;

/// <summary>
/// Two bearer schemes on the access key: the default one accepts only access tokens; the OTP one
/// accepts only temp tokens and guards POST /auth/pin/set. Grant tokens use a third key (Grants
/// module) and are never valid bearer tokens. Challenges and forbids are written as A.1 envelopes.
/// </summary>
internal static class AuthSchemes
{
    public const string Access = JwtBearerDefaults.AuthenticationScheme;
    public const string OtpTemp = "OtpTemp";
    public const string OtpTempPolicy = "OtpTemp";

    public static void AddAuthSchemes(this IServiceCollection services, JwtSettings settings, SigningKeys keys)
    {
        services
            .AddAuthentication(Access)
            .AddJwtBearer(Access, o => Configure(o, settings, keys, settings.AccessAudience, TokenTypes.Access))
            .AddJwtBearer(OtpTemp, o => Configure(o, settings, keys, settings.TempAudience, TokenTypes.Temp));

        services.AddAuthorization(o =>
            o.AddPolicy(OtpTempPolicy, p => p.AddAuthenticationSchemes(OtpTemp).RequireAuthenticatedUser()));
    }

    private static void Configure(JwtBearerOptions o, JwtSettings settings, SigningKeys keys, string audience, string type)
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = audience,
            IssuerSigningKey = keys.Access,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                if (ctx.Principal?.FindFirst(TokenTypes.Claim)?.Value != type)
                    ctx.Fail($"Expected a {type} token");
                return Task.CompletedTask;
            },
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                var message = ctx.AuthenticateFailure switch
                {
                    SecurityTokenExpiredException => type == TokenTypes.Temp
                        ? "Verification expired; request a new OTP"
                        : "Access token expired",
                    not null => "Invalid token",
                    null when string.IsNullOrEmpty(ctx.Request.Headers.Authorization) => "Missing access token",
                    _ => "Invalid token",
                };
                await WriteAsync(ctx.HttpContext, ErrorCode.Unauthenticated, message);
            },
            OnForbidden = ctx => WriteAsync(ctx.HttpContext, ErrorCode.Forbidden, "Not allowed"),
        };
    }

    private static async Task WriteAsync(HttpContext http, ErrorCode code, string message)
    {
        if (http.Response.HasStarted) return;
        http.Response.StatusCode = code.HttpStatus();
        var options = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        await http.Response.WriteAsJsonAsync(
            new ApiErrorEnvelope(new ApiError(code.Wire(), message, new Dictionary<string, string>())), options);
    }
}
