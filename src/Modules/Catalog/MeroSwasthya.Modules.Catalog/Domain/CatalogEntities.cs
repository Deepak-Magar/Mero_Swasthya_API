using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Catalog.Domain;

/// <summary>A.2 Facility.type.</summary>
public enum FacilityType
{
    [WireName("health_post")] HealthPost,
    [WireName("phcc")] Phcc,
    [WireName("hospital")] Hospital,
    [WireName("birthing_centre")] BirthingCentre,
}

/// <summary>A.2 CodeListItem.kind.</summary>
public enum CodeListKind
{
    [WireName("complaint")] Complaint,
    [WireName("diagnosis")] Diagnosis,
    [WireName("drug")] Drug,
    [WireName("dangerSign")] DangerSign,
    [WireName("riskFactor")] RiskFactor,
}

/// <summary>A.2 Facility (read-only, seeded for one district).</summary>
internal sealed class Facility
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public FacilityType Type { get; set; }
    public bool HasBirthingCentre { get; set; }
    public string? Phone { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public required string Municipality { get; set; }
}

/// <summary>A.2 CodeListItem. <see cref="MetaJson"/> is jsonb: drug → { strength, form }, otherwise null.</summary>
internal sealed class CodeListItem
{
    public CodeListKind Kind { get; init; }
    public required string Code { get; init; }
    public required string LabelEn { get; set; }
    public required string LabelNp { get; set; }
    public string? MetaJson { get; set; }

    /// <summary>Preserves the order of the source list so the app's pickers do not reshuffle.</summary>
    public int SortOrder { get; set; }
}

/// <summary>Key/value settings owned by Catalog, e.g. <c>codelist_version</c>.</summary>
internal sealed class CatalogSetting
{
    public const string CodeListVersion = "codelist_version";

    public required string Key { get; init; }
    public required string Value { get; set; }
}
