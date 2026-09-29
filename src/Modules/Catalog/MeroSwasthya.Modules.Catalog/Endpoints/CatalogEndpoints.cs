using System.Globalization;
using System.Text.Json;
using MeroSwasthya.Modules.Catalog.Application;
using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Modules.Catalog.Infrastructure;
using MeroSwasthya.Shared;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Catalog.Endpoints;

/// <summary>A.2 CodeListItem.</summary>
internal sealed record CodeListItemDto(CodeListKind Kind, string Code, string LabelEn, string LabelNp, JsonElement? Meta);

internal sealed record CodeListsResponse(string Version, IReadOnlyList<CodeListItemDto> Items);

/// <summary>A.4 GET /config + contract addendum §6 (three additive flags).</summary>
internal sealed record ConfigResponse(
    string SmsMode,
    bool AiSummaryEnabled,
    bool OtpDemo,
    string RulesVersion,
    string CodelistVersion,
    bool NidEnabled,
    bool HmisExportEnabled,
    bool CouncilVerifyEnabled);

internal static class CatalogEndpoints
{
    public const int NearbyDefaultLimit = 5;
    public const int NearbyMaxLimit = 50;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/codelists", async (string? kind, CatalogDbContext db, ICodeListLookup lookup, CancellationToken ct) =>
            {
                CodeListKind? filter = null;
                if (kind is not null)
                {
                    if (!WireEnum.TryParse<CodeListKind>(kind, out var parsed))
                        throw AppException.Validation("kind", $"Must be one of: {string.Join(", ", WireEnum.Names<CodeListKind>())}");
                    filter = parsed;
                }

                var rows = await db.CodeListItems.AsNoTracking()
                    .Where(c => filter == null || c.Kind == filter)
                    .OrderBy(c => c.SortOrder)
                    .ToListAsync(ct);
                var items = rows.Select(c => new CodeListItemDto(c.Kind, c.Code, c.LabelEn, c.LabelNp, ParseMeta(c.MetaJson))).ToList();
                return ApiResults.Ok(new CodeListsResponse(await lookup.VersionAsync(ct), items));
            })
            .WithTags("Catalog")
            .AllowAnonymous();

        // A.4: data is the RULES object exactly as in A.5 — served verbatim from the app's own rules.json.
        api.MapGet("/rules", (IRulesProvider rules) => ApiResults.Ok(rules.Document))
            .WithTags("Catalog")
            .AllowAnonymous();

        api.MapGet("/config", async (FeatureFlags features, IRulesProvider rules, ICodeListLookup lookup, CancellationToken ct) =>
                ApiResults.Ok(new ConfigResponse(
                    features.SmsIsMock ? "mock" : features.SmsMode,
                    features.AiSummaryEnabled,
                    features.OtpDemo,
                    rules.Version,
                    await lookup.VersionAsync(ct),
                    features.NidEnabled,
                    features.HmisExportEnabled,
                    features.CouncilVerifyEnabled)))
            .WithTags("Catalog")
            .AllowAnonymous();

        var facilities = api.MapGroup("/facilities").WithTags("Catalog").RequireAuthorization();

        // Not in A.4 (additive): the whole district list, for pickers such as the birth-plan facility.
        facilities.MapGet("", async (CatalogDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Facilities.AsNoTracking().OrderBy(f => f.Id).ToListAsync(ct);
            return ApiResults.Ok(new ItemsResponse<FacilityDto>(
                rows.Select(f => FacilityDto.From(FacilityDirectory.ToInfo(f))).ToList()));
        });

        facilities.MapGet("/nearby", async (
            string? lat, string? lng, string? birthing, string? limit, IFacilityDirectory directory, CancellationToken ct) =>
        {
            var query = ParseNearby(lat, lng, birthing, limit);
            var nearest = await directory.NearestAsync(query.Lat, query.Lng, query.BirthingOnly, query.Limit, ct);
            return ApiResults.Ok(new ItemsResponse<FacilityDto>(
                nearest.Select(n => FacilityDto.From(n.Facility, Math.Round(n.DistanceKm, 1))).ToList()));
        });
    }

    private static JsonElement? ParseMeta(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    internal sealed record NearbyQuery(double Lat, double Lng, bool BirthingOnly, int Limit);

    internal static NearbyQuery ParseNearby(string? lat, string? lng, string? birthing, string? limit)
    {
        var errors = new Dictionary<string, string>();

        double Coordinate(string? raw, string field, double bound)
        {
            if (string.IsNullOrWhiteSpace(raw)) { errors[field] = "Required"; return 0; }
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || Math.Abs(v) > bound)
            {
                errors[field] = $"Must be a number between -{bound} and {bound}";
                return 0;
            }
            return v;
        }

        var latValue = Coordinate(lat, "lat", 90);
        var lngValue = Coordinate(lng, "lng", 180);

        var birthingOnly = false;
        if (!string.IsNullOrWhiteSpace(birthing) && !bool.TryParse(birthing, out birthingOnly))
            errors["birthing"] = "Must be true or false";

        var limitValue = NearbyDefaultLimit;
        try { limitValue = Cursors.Limit(limit, NearbyDefaultLimit, NearbyMaxLimit); }
        catch (AppException) { errors["limit"] = $"Must be an integer between 1 and {NearbyMaxLimit}"; }

        if (errors.Count > 0) throw AppException.Validation(errors);
        return new NearbyQuery(latValue, lngValue, birthingOnly, limitValue);
    }
}
