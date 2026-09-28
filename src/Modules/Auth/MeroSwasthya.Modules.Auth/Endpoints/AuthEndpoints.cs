using MeroSwasthya.Modules.Auth.Application;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MeroSwasthya.Modules.Auth.Endpoints;

/// <summary>A.4 "Auth" + GET /me.</summary>
internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/otp/request", async (OtpRequest body, AuthService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.RequestOtpAsync(body, ct)))
            .Validate<OtpRequest>()
            .AllowAnonymous();

        auth.MapPost("/otp/verify", async (OtpVerifyRequest body, AuthService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.VerifyOtpAsync(body, ct)))
            .Validate<OtpVerifyRequest>()
            .AllowAnonymous();

        // Bearer tempToken (from /otp/verify), not an access token.
        auth.MapPost("/pin/set", async (PinSetRequest body, HttpContext http, AuthService svc, CancellationToken ct) =>
            {
                var phone = TokenService.Subject(http.User) ?? throw AppException.Unauthenticated();
                return ApiResults.Ok(await svc.SetPinAsync(phone, body, ct));
            })
            .Validate<PinSetRequest>()
            .RequireAuthorization(AuthSchemes.OtpTempPolicy);

        auth.MapPost("/pin/login", async (PinLoginRequest body, AuthService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.LoginAsync(body, ct)))
            .Validate<PinLoginRequest>()
            .AllowAnonymous();

        auth.MapPost("/refresh", async (RefreshRequest body, AuthService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.RefreshAsync(body, ct)))
            .Validate<RefreshRequest>()
            .AllowAnonymous();

        auth.MapPost("/provider/activate", async (ActivateRequest body, ICurrentUser me, AuthService svc, CancellationToken ct) =>
            {
                var user = await me.GetAsync(ct);
                return ApiResults.Ok(new UserResponse(await svc.ActivateAsync(user.Id, body, ct)));
            })
            .Validate<ActivateRequest>()
            .RequireAuthorization();

        api.MapGet("/me", async (ICurrentUser me, AuthService svc, CancellationToken ct) =>
            {
                var user = await me.GetAsync(ct);
                return ApiResults.Ok(new UserResponse(await svc.MeAsync(user.Id, ct)));
            })
            .WithTags("Auth")
            .RequireAuthorization();
    }
}
