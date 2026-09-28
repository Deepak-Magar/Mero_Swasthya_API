using MeroSwasthya.Modules.Patients.Application;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Paging;
using MeroSwasthya.Shared.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MeroSwasthya.Modules.Patients.Endpoints;

/// <summary>A.4 "Patients (family profiles)" — list, create, detail + summary, patch, timeline.</summary>
internal static class PatientEndpoints
{
    public const int TimelineDefaultLimit = 50;
    public const int TimelineMaxLimit = 200;

    public static void Map(IEndpointRouteBuilder api)
    {
        var patients = api.MapGroup("/patients").WithTags("Patients").RequireAuthorization();

        patients.MapGet("", async (PatientService svc, CancellationToken ct) =>
            ApiResults.Ok(new ItemsResponse<PatientDto>(await svc.ListAsync(ct))));

        patients.MapPost("", async (CreatePatientRequest body, PatientService svc, CancellationToken ct) =>
                ApiResults.Ok(new PatientResponse(await svc.CreateAsync(body, ct))))
            .Validate<CreatePatientRequest>();

        patients.MapGet("/{id}", async (string id, PatientService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetAsync(id, ct)));

        patients.MapPatch("/{id}", async (string id, PatchPatientRequest body, PatientService svc, CancellationToken ct) =>
                ApiResults.Ok(new PatientResponse(await svc.PatchAsync(id, body, ct))))
            .Validate<PatchPatientRequest>();

        patients.MapGet("/{id}/timeline", async (string id, string? limit, string? before, PatientService svc, CancellationToken ct) =>
        {
            var pageSize = Cursors.Limit(limit, TimelineDefaultLimit, TimelineMaxLimit);
            var cursor = Cursors.Timestamp(before, "before");
            return ApiResults.Ok(await svc.TimelineAsync(id, cursor, pageSize, ct));
        });

        // TODO(Grants): GET /patients/{id}/audit (owner only) — AuditEntry lives in the Grants module.
    }
}
