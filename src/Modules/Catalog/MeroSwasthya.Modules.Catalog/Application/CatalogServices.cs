using System.Text.Json;
using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Modules.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Catalog.Application;

/// <summary>Loaded once from the embedded copy of the app's assets/rules.json.</summary>
internal sealed class RulesProvider : IRulesProvider
{
    public RulesProvider()
    {
        using var doc = CatalogResources.Parse(CatalogResources.Rules);
        Document = doc.RootElement.Clone();
        Version = Document.GetProperty("version").GetString()
                  ?? throw new InvalidOperationException("rules.json has no version");
    }

    public string Version { get; }
    public JsonElement Document { get; }
}

internal sealed class CodeListLookup(CatalogDbContext db) : ICodeListLookup
{
    public async Task<IReadOnlyDictionary<string, CodeLabel>> LabelsAsync(
        CodeListKind kind, IEnumerable<string> codes, CancellationToken ct = default)
    {
        var wanted = codes.Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<string, CodeLabel>();
        return await db.CodeListItems.AsNoTracking()
            .Where(c => c.Kind == kind && wanted.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, c => new CodeLabel(c.Code, c.LabelEn, c.LabelNp), ct);
    }

    public async Task<string> VersionAsync(CancellationToken ct = default) =>
        await db.Settings.AsNoTracking()
            .Where(s => s.Key == CatalogSetting.CodeListVersion)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct) ?? "";
}

internal sealed class FacilityDirectory(CatalogDbContext db) : IFacilityDirectory
{
    public async Task<FacilityInfo?> FindAsync(string facilityId, CancellationToken ct = default)
    {
        var f = await db.Facilities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == facilityId, ct);
        return f is null ? null : ToInfo(f);
    }

    public async Task<IReadOnlyList<(FacilityInfo Facility, double DistanceKm)>> NearestAsync(
        double lat, double lng, bool birthingOnly, int limit, CancellationToken ct = default)
    {
        // One district: a handful of rows, so distance is computed in memory rather than with PostGIS.
        var facilities = await db.Facilities.AsNoTracking()
            .Where(f => !birthingOnly || f.HasBirthingCentre)
            .ToListAsync(ct);
        return facilities
            .Select(f => (Facility: ToInfo(f), DistanceKm: Geo.HaversineKm(lat, lng, f.Lat, f.Lng)))
            .OrderBy(x => x.DistanceKm)
            .ThenBy(x => x.Facility.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    internal static FacilityInfo ToInfo(Facility f) =>
        new(f.Id, f.Name, f.Type, f.HasBirthingCentre, f.Phone, f.Lat, f.Lng, f.Municipality);
}

public static class Geo
{
    /// <summary>IUGG mean Earth radius.</summary>
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>Great-circle distance between two WGS-84 points (A.4 /facilities/nearby).</summary>
    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double deg) => deg * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
