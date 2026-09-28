using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Patients.Domain;

/// <summary>A.2 Patient.sex.</summary>
public enum Sex
{
    [WireName("female")] Female,
    [WireName("male")] Male,
    [WireName("other")] Other,
}

/// <summary>A.2 Patient — a family profile. Syncable: client-generated id, server-assigned version/updatedAt.</summary>
internal sealed class Patient
{
    public required string Id { get; init; }
    public required string OwnerUserId { get; init; }
    public required string Name { get; set; }
    public Sex Sex { get; set; }
    public DateOnly Dob { get; set; }
    public string? BloodGroup { get; set; }
    public int? Ward { get; set; }
    public string? Municipality { get; set; }
    public List<string> Allergies { get; set; } = [];
    public List<string> ChronicConditions { get; set; } = [];
    public string? EmergencyContactPhone { get; set; }

    /// <summary>Starts at 1; +1 on every accepted update. Also the EF concurrency token.</summary>
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; init; }
}
