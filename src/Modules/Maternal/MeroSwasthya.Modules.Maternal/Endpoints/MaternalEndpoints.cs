using MeroSwasthya.Modules.Maternal.Application;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MeroSwasthya.Modules.Maternal.Endpoints;

/// <summary>A.4 "Maternal".</summary>
internal static class MaternalEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var patientPregnancies = api.MapGroup("/patients/{id}/pregnancies").WithTags("Maternal").RequireAuthorization();

        patientPregnancies.MapPost("", async (string id, CreatePregnancyRequest body, PregnancyService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.CreateAsync(id, body, ct)))
            .Validate<CreatePregnancyRequest>();

        // Additive (not in A.4): all pregnancies of a patient, newest first.
        patientPregnancies.MapGet("", async (string id, PregnancyService svc, CancellationToken ct) =>
            ApiResults.Ok(new ItemsResponse<PregnancyDto>(await svc.ListAsync(id, ct))));

        var pregnancies = api.MapGroup("/pregnancies").WithTags("Maternal").RequireAuthorization();

        pregnancies.MapGet("/{id}", async (string id, PregnancyService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetAsync(id, ct)));

        pregnancies.MapPatch("/{id}", async (string id, PatchPregnancyRequest body, PregnancyService svc, CancellationToken ct) =>
                ApiResults.Ok(new PregnancyResponse(await svc.PatchAsync(id, body, ct))))
            .Validate<PatchPregnancyRequest>();
    }
}
