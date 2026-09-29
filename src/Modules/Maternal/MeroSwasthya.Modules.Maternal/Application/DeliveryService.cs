using FluentValidation;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Modules.Maternal.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeroSwasthya.Modules.Maternal.Application;

/// <summary>A.4 POST /pregnancies/:id/delivery body.</summary>
internal sealed class RecordDeliveryRequest
{
    public string? Id { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public string? Place { get; init; }
    public string? Mode { get; init; }
    public string? Outcome { get; init; }
    public double? BabyWeightKg { get; init; }
    public string? BabySex { get; init; }
    public List<string>? Complications { get; init; }
}

internal sealed record DeliveryRecordedResponse(DeliveryDto Delivery, PregnancyDto Pregnancy);

internal sealed class RecordDeliveryRequestValidator : AbstractValidator<RecordDeliveryRequest>
{
    public const int MaxComplications = 20;

    public RecordDeliveryRequestValidator(IClock clock)
    {
        RuleFor(x => x.Id).NotEmpty().ClientId();
        RuleFor(x => x.DeliveredAt).NotNull()
            .Must(d => d is null || d.Value <= clock.UtcNow.AddDays(1)).WithMessage("'deliveredAt' cannot be in the future");
        RuleFor(x => x.Place).NotEmpty().WireEnum<RecordDeliveryRequest, DeliveryPlace>();
        RuleFor(x => x.Mode).NotEmpty().WireEnum<RecordDeliveryRequest, DeliveryMode>();
        RuleFor(x => x.Outcome).NotEmpty().WireEnum<RecordDeliveryRequest, DeliveryOutcome>();
        RuleFor(x => x.BabyWeightKg).InclusiveBetween(0.3, 8.0).When(x => x.BabyWeightKg is not null);
        RuleFor(x => x.BabySex).WireEnum<RecordDeliveryRequest, BabySex>();
        RuleFor(x => x.Complications)
            .Must(l => l is null || l.Count <= MaxComplications).WithMessage($"At most {MaxComplications} items")
            .Must(l => l is null || l.All(c => !string.IsNullOrWhiteSpace(c) && c.Length <= 200))
            .WithMessage("Items must be non-blank text of at most 200 characters");
    }
}

/// <summary>
/// A.4 "Close the pregnancy": stores the delivery (idempotent on the client id), sets the pregnancy to
/// <c>delivered</c>, and marks the contacts that never happened as not applicable (soft-deleted, so a
/// synced phone drops them from its schedule).
/// </summary>
internal sealed class DeliveryService(
    MaternalDbContext db,
    PregnancyService pregnancies,
    IPatientAccess access,
    ICurrentUser currentUser,
    IRulesService rules,
    IClock clock)
{
    public async Task<DeliveryRecordedResponse> RecordAsync(string pregnancyId, RecordDeliveryRequest request, CancellationToken ct)
    {
        var pregnancy = await pregnancies.LoadAsync(pregnancyId, ct, track: true);
        await access.RequireAppendAsync(pregnancy.PatientId, ct);
        var user = await currentUser.GetAsync(ct);

        var existing = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.Id, ct);
        if (existing is not null) return await IdempotentAsync(existing, pregnancy, ct);

        // A.3: delivery on a non-active pregnancy is a rule violation.
        if (pregnancy.Status != PregnancyStatus.Active)
            throw AppException.RuleViolation($"This pregnancy is already {pregnancy.Status.ToWire()}");

        var now = clock.UtcNow;
        var delivery = new Delivery
        {
            Id = request.Id!,
            PregnancyId = pregnancy.Id,
            PatientId = pregnancy.PatientId,
            DeliveredAt = request.DeliveredAt!.Value,
            Place = WireEnum.Parse<DeliveryPlace>(request.Place!),
            Mode = WireEnum.Parse<DeliveryMode>(request.Mode!),
            Outcome = WireEnum.Parse<DeliveryOutcome>(request.Outcome!),
            BabyWeightKg = request.BabyWeightKg,
            BabySex = request.BabySex is null ? null : WireEnum.Parse<BabySex>(request.BabySex),
            Complications = (request.Complications ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
            RecordedByUserId = user.Id,
            Version = 1,
            UpdatedAt = now,
            CreatedAt = now,
        };
        db.Deliveries.Add(delivery);

        pregnancy.Status = PregnancyStatus.Delivered;
        pregnancy.Version++;
        pregnancy.UpdatedAt = now;

        var contacts = await db.AncContacts.Where(c => c.PregnancyId == pregnancy.Id && !c.Deleted).ToListAsync(ct);
        foreach (var contact in contacts.Where(c => c.DoneAt is null))
        {
            contact.Deleted = true;
            contact.Version++;
            contact.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var winner = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == request.Id, ct);
            return await IdempotentAsync(winner, await pregnancies.LoadAsync(pregnancyId, ct), ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw AppException.RuleViolation("This pregnancy was changed by someone else; reload and try again");
        }

        return new DeliveryRecordedResponse(delivery.ToDto(), pregnancy.ToDto(contacts, rules, clock.TodayUtc));
    }

    private async Task<DeliveryRecordedResponse> IdempotentAsync(Delivery existing, Pregnancy pregnancy, CancellationToken ct)
    {
        if (existing.PregnancyId != pregnancy.Id)
            throw AppException.Validation("id", "This id is already used by a delivery of another pregnancy");
        db.ChangeTracker.Clear();
        var current = await pregnancies.LoadAsync(pregnancy.Id, ct);
        var contacts = await pregnancies.ContactsAsync(pregnancy.Id, ct);
        return new DeliveryRecordedResponse(existing.ToDto(), current.ToDto(contacts, rules, clock.TodayUtc));
    }
}
