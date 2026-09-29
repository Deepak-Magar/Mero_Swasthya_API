using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Modules.Reminders.Contracts;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MeroSwasthya.Modules.Reminders.Endpoints;

/// <summary>A.4 "Reminders": the patient's list, plus additive done / cancel actions.</summary>
internal static class ReminderEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/patients/{id}/reminders", async (string id, string? limit, ReminderService svc, CancellationToken ct) =>
                ApiResults.Ok(new ItemsResponse<ReminderDto>(await svc.ListAsync(id, Cursors.Limit(limit, DefaultLimit, MaxLimit), ct))))
            .WithTags("Reminders")
            .RequireAuthorization();

        var reminders = api.MapGroup("/reminders").WithTags("Reminders").RequireAuthorization();

        reminders.MapPost("/{id}/done", async (string id, ReminderService svc, CancellationToken ct) =>
            ApiResults.Ok(new ReminderResponse(await svc.MarkDoneAsync(id, ct))));

        reminders.MapPost("/{id}/cancel", async (string id, ReminderService svc, CancellationToken ct) =>
            ApiResults.Ok(new ReminderResponse(await svc.CancelAsync(id, ct))));
    }
}
