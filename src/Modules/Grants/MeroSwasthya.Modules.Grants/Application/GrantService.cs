using MeroSwasthya.Modules.Audit.Contracts;
using MeroSwasthya.Modules.Auth.Contracts;
using MeroSwasthya.Modules.Grants.Contracts;
using MeroSwasthya.Modules.Grants.Domain;
using MeroSwasthya.Modules.Grants.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Ids;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Grants.Application;

/// <summary>A.4 "Access grants (QR)" + A.7.</summary>
internal sealed class GrantService(
    GrantsDbContext db,
    GrantTokens tokens,
    ICurrentUser currentUser,
    IPatientAccess access,
    IPatientDirectory patients,
    IPatientSummaryService summaries,
    IPatientTimelineService timelines,
    IEnumerable<IActivePregnancySource> pregnancies,
    IPinVerifier pins,
    IAuditWriter audit,
    IClock clock)
{
    public const int BundleTimelineSize = 50;

    public async Task<CreateGrantResponse> CreateAsync(CreateGrantRequest request, CancellationToken ct)
    {
        var owner = await currentUser.GetAsync(ct);
        var patient = await access.RequireOwnerAsync(request.PatientId!, ct);
        var now = clock.UtcNow;

        // A.4: 20 per patient per hour → 429.
        var recent = await db.Grants.CountAsync(g => g.PatientId == patient.Id && g.CreatedAt > now.AddHours(-1), ct);
        if (recent >= GrantLimits.PerPatientPerHour)
            throw AppException.RateLimited("Too many share codes for this patient; try again later");

        var ttl = request.TtlMinutes ?? GrantLimits.DefaultTtlMinutes;
        var longLived = ttl >= GrantLimits.LongLivedFromMinutes;
        var scope = request.Scope is null
            ? longLived ? GrantScope.Read : GrantScope.Append
            : WireEnum.Parse<GrantScope>(request.Scope);

        var grant = new AccessGrant
        {
            Id = Ids.New("g"),
            PatientId = patient.Id,
            Scope = scope,
            Sections = (request.Sections ?? []).Distinct().ToList(),
            Jti = Guid.NewGuid().ToString("N"),
            CreatedByUserId = owner.Id,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ttl),
            LongLived = longLived,
        };
        db.Grants.Add(grant);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(patient.Id, AuditAction.GrantCreated, ct);

        var token = tokens.Create(grant);
        return new CreateGrantResponse(ToDto(grant, token), token, GrantTokens.QrPrefix + token);
    }

    public async Task<RedeemResponse> RedeemAsync(RedeemGrantRequest request, CancellationToken ct)
    {
        var provider = await currentUser.GetAsync(ct);
        if (!provider.IsHealthWorker)
            throw AppException.Forbidden("Only a provider or FCHV can redeem a share code");

        var claims = await tokens.ReadAsync(request.QrPayload!)
                     ?? throw AppException.Validation("qrPayload", "Not a Mero Swasthya share code");
        var grant = await db.Grants.FirstOrDefaultAsync(g => g.Id == claims.GrantId && g.Jti == claims.Jti, ct)
                    ?? throw AppException.NotFound("Grant");

        var now = clock.UtcNow;
        if (grant.RevokedAt is not null)
            throw new AppException(ErrorCode.GrantExpired, "This share code has been withdrawn");

        if (grant.RedeemedByUserId == provider.Id)
        {
            // Same provider scanning again: idempotent while the 24 h window lasts.
            if (grant.AccessUntil <= now)
                throw new AppException(ErrorCode.GrantExpired, "Access window has ended; ask for a new code");
            return await BundleAsync(grant, ct);
        }

        // Same order as mock_api.dart: an expired code is GRANT_EXPIRED whoever holds it.
        if (grant.ExpiresAt <= now)
            throw new AppException(ErrorCode.GrantExpired, "QR expired, ask for a new one");
        if (grant.RedeemedByUserId is not null)
            throw new AppException(ErrorCode.AlreadyRedeemed, "This share code was already used by another health worker");

        if (grant.LongLived)
            await RequireOwnerPinAsync(grant, request.Pin, ct);

        // Conditional update: two providers scanning the same code at once — exactly one wins.
        var claimed = await db.Grants
            .Where(g => g.Id == grant.Id && g.RedeemedByUserId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.RedeemedByUserId, provider.Id)
                .SetProperty(g => g.RedeemedAt, now)
                .SetProperty(g => g.AccessUntil, now + GrantLimits.AccessWindow), ct);
        await db.Entry(grant).ReloadAsync(ct);
        if (claimed == 0 && grant.RedeemedByUserId != provider.Id)
            throw new AppException(ErrorCode.AlreadyRedeemed, "This share code was already used by another health worker");

        if (claimed == 1)
            await audit.WriteAsync(grant.PatientId, AuditAction.GrantRedeemed, ct);
        return await BundleAsync(grant, ct);
    }

    public async Task<GrantDto> RevokeAsync(string grantId, CancellationToken ct)
    {
        var grant = await db.Grants.FirstOrDefaultAsync(g => g.Id == grantId, ct) ?? throw AppException.NotFound("Grant");
        await access.RequireOwnerAsync(grant.PatientId, ct);

        if (grant.RevokedAt is null)
        {
            grant.RevokedAt = clock.UtcNow;
            grant.AccessUntil = null; // provider access ends immediately (same as mock_api.dart)
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(grant.PatientId, AuditAction.GrantRevoked, ct);
        }
        return ToDto(grant);
    }

    /// <summary>A.7: the printed card needs the patient standing there to say their PIN. details.pin = required | invalid (as the mock).</summary>
    private async Task RequireOwnerPinAsync(AccessGrant grant, string? pin, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(pin))
            throw new AppException(ErrorCode.Forbidden, "This card needs the patient PIN", new Dictionary<string, string> { ["pin"] = "required" });

        var patient = await patients.FindAsync(grant.PatientId, ct) ?? throw AppException.NotFound("Patient");
        if (!await pins.VerifyAsync(patient.OwnerUserId, pin, ct))
            throw new AppException(ErrorCode.Forbidden, "Wrong PIN", new Dictionary<string, string> { ["pin"] = "invalid" });
    }

    /// <summary>
    /// The redeem bundle, filtered server-side by <c>sections</c> (addendum §4). The patient row always
    /// travels (it carries the allergies); a withheld summary is sent empty, never null.
    /// </summary>
    private async Task<RedeemResponse> BundleAsync(AccessGrant grant, CancellationToken ct)
    {
        var patient = await patients.FindAsync(grant.PatientId, ct);
        if (patient is null || patient.Deleted) throw AppException.NotFound("Patient");

        bool Shares(GrantSection section) => grant.Sections.Count == 0 || grant.Sections.Contains(section.ToWire());

        var summary = Shares(GrantSection.Summary) ? await summaries.BuildAsync(patient, ct) : PatientSummaryDto.Empty;

        // Over-fetch so filtering by section still leaves up to 50 items.
        var page = await timelines.GetPageAsync(patient.Id, null, 200, ct);
        var timeline = page.Items.Where(i => i.Kind switch
            {
                TimelineKind.Visit => Shares(GrantSection.Visits),
                TimelineKind.Document => Shares(GrantSection.Documents),
                TimelineKind.PregnancyRegistered or TimelineKind.AncContact or TimelineKind.Delivery => Shares(GrantSection.Pregnancy),
                TimelineKind.Immunisation or TimelineKind.Growth => Shares(GrantSection.Child),
                _ => true,
            })
            .Take(BundleTimelineSize)
            .ToList();

        ActivePregnancySnapshot? pregnancy = null;
        if (Shares(GrantSection.Pregnancy))
            foreach (var source in pregnancies)
                pregnancy ??= await source.GetAsync(patient.Id, ct);

        return new RedeemResponse(ToDto(grant), patient, summary, timeline, pregnancy?.Pregnancy, pregnancy?.AncContacts ?? []);
    }

    internal static GrantDto ToDto(AccessGrant g, string? token = null) => new(
        g.Id, g.PatientId, g.Scope, token, g.ExpiresAt, g.RedeemedByUserId, g.RedeemedAt, g.RevokedAt, g.AccessUntil,
        g.LongLived, g.Sections.Select(WireEnum.Parse<GrantSection>).ToList());
}
