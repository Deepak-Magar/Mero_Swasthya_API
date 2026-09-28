using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Grants.Domain;

/// <summary>A.2 AccessGrant.scope — append implies read.</summary>
public enum GrantScope
{
    [WireName("read")] Read,
    [WireName("append")] Append,
}

/// <summary>Contract addendum §4 — what a grant shares. Empty = the whole record.</summary>
public enum GrantSection
{
    [WireName("summary")] Summary,
    [WireName("visits")] Visits,
    [WireName("documents")] Documents,
    [WireName("pregnancy")] Pregnancy,
    [WireName("child")] Child,
    [WireName("audit")] Audit,
}

/// <summary>A.2 AccessGrant. The token itself is never stored — only its <see cref="Jti"/>.</summary>
internal sealed class AccessGrant
{
    public required string Id { get; init; }
    public required string PatientId { get; init; }
    public GrantScope Scope { get; init; }
    public List<string> Sections { get; init; } = [];

    /// <summary>JWT id of the one token issued for this grant; redeem must present exactly this token.</summary>
    public required string Jti { get; init; }
    public required string CreatedByUserId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }

    /// <summary>A.7 printed card (ttl ≥ 1 day, scope read): redeem also needs the patient's PIN.</summary>
    public bool LongLived { get; init; }

    public string? RedeemedByUserId { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? AccessUntil { get; set; }
}
