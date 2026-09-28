using System.Text.Json;
using MeroSwasthya.Modules.Catalog.Domain;

namespace MeroSwasthya.Modules.Catalog.Contracts;

/// <summary>A code's two labels, e.g. E11 → "Type 2 diabetes" / "मधुमेह".</summary>
public sealed record CodeLabel(string Code, string LabelEn, string LabelNp);

/// <summary>Label lookups for other modules (summary activeProblems, prescription drugName, …).</summary>
public interface ICodeListLookup
{
    /// <summary>Labels for the requested codes of one kind; unknown codes are simply absent.</summary>
    Task<IReadOnlyDictionary<string, CodeLabel>> LabelsAsync(CodeListKind kind, IEnumerable<string> codes, CancellationToken ct = default);

    Task<string> VersionAsync(CancellationToken ct = default);
}

/// <summary>A.2 Facility as other modules see it.</summary>
public sealed record FacilityInfo(
    string Id,
    string Name,
    FacilityType Type,
    bool HasBirthingCentre,
    string? Phone,
    double Lat,
    double Lng,
    string Municipality);

public interface IFacilityDirectory
{
    Task<FacilityInfo?> FindAsync(string facilityId, CancellationToken ct = default);

    /// <summary>Nearest first, straight-line (Haversine) distance in km. Maternal uses it for nearestReferral.</summary>
    Task<IReadOnlyList<(FacilityInfo Facility, double DistanceKm)>> NearestAsync(
        double lat, double lng, bool birthingOnly, int limit, CancellationToken ct = default);
}

/// <summary>The A.5 RULES document exactly as shipped in the app's assets/rules.json.</summary>
public interface IRulesProvider
{
    string Version { get; }

    /// <summary>The whole document, verbatim. Maternal parses ancSchedule / dangerSigns / riskFactors from it.</summary>
    JsonElement Document { get; }
}
