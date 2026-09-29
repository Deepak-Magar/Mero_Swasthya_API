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

        // Additive (not in A.4): the schedule on its own.
        pregnancies.MapGet("/{id}/contacts", async (string id, AncContactService svc, CancellationToken ct) =>
            ApiResults.Ok(new ItemsResponse<AncContactDto>(await svc.ListAsync(id, ct))));

        // contactNo is bound as text so that "9" or "x" is the A.3 422 RULE_VIOLATION rather than a routing 404.
        pregnancies.MapPut("/{id}/contacts/{contactNo}", async (
                string id, string contactNo, RecordContactRequest body, AncContactService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.RecordAsync(id, contactNo, body, ct)))
            .Validate<RecordContactRequest>();
    }
}
