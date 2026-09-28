using MeroSwasthya.Shared.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MeroSwasthya.Shared.Errors;

/// <summary>Body-less 4xx responses produced by routing or the framework get the envelope too.</summary>
public static class EnvelopeStatusCodes
{
    public static IApplicationBuilder UseEnvelopeStatusCodes(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            var status = http.Response.StatusCode;
            var (code, message) = status switch
            {
                404 => (ErrorCode.NotFound.Wire(), $"No route for {http.Request.Method} {http.Request.Path}"),
                405 => (ErrorCode.NotFound.Wire(), $"Method {http.Request.Method} is not allowed on {http.Request.Path}"),
                401 => (ErrorCode.Unauthenticated.Wire(), "Missing or invalid access token"),
                403 => (ErrorCode.Forbidden.Wire(), "Not allowed"),
                415 => (ErrorCode.ValidationError.Wire(), "Content-Type must be application/json"),
                429 => (ErrorCode.RateLimited.Wire(), "Too many requests"),
                >= 400 and < 500 => (ErrorCode.ValidationError.Wire(), "Bad request"),
                _ => (ErrorCode.Internal.Wire(), "Unexpected server error"),
            };
            var options = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            await http.Response.WriteAsJsonAsync(
                new ApiErrorEnvelope(new ApiError(code, message, GlobalExceptionHandler.EmptyDetails)), options);
        });
}
