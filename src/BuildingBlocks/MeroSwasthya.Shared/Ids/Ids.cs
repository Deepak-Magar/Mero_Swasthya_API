using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MeroSwasthya.Shared.Ids;

/// <summary>RFC 4122 §4.3 name-based UUID, version 5 (SHA-1).</summary>
public static class Uuid5
{
    public static Guid Create(Guid @namespace, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var nsBytes = @namespace.ToByteArray(bigEndian: true);

        var input = new byte[nsBytes.Length + nameBytes.Length];
        nsBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, nsBytes.Length);

        var hash = SHA1.HashData(input);
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }
}

public static partial class Ids
{
    /// <summary>A.8.15 — the namespace both the app and the server use for deterministic ids.</summary>
    public static readonly Guid DeterministicNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>A.8.15: <c>uuidv5(ns, pregnancyId + ":" + contactNo)</c>, lower-case, hyphenated.</summary>
    public static string AncContactId(string pregnancyId, int contactNo) =>
        Uuid5.Create(DeterministicNamespace, $"{pregnancyId}:{contactNo}").ToString("D");

    /// <summary>Contract addendum §1: <c>uuidv5(ns, patientId + ":" + vaccineCode + ":" + doseNo)</c>, doseNo unpadded.</summary>
    public static string ImmunisationId(string patientId, string vaccineCode, int doseNo) =>
        Uuid5.Create(DeterministicNamespace, $"{patientId}:{vaccineCode}:{doseNo}").ToString("D");

    /// <summary>Server-generated id with a readable prefix, e.g. <c>u_1b4e…</c> (User), <c>g_…</c> (grant).</summary>
    public static string New(string prefix) => $"{prefix}_{Guid.NewGuid():D}";

    /// <summary>
    /// Client-generated ids (A.1) are UUID v4 in practice, but the Part A examples carry a readable
    /// prefix (<c>p_a1a1a1a1-…</c>, <c>rx_0001</c>), so the server accepts any 1–64 char token of
    /// letters, digits, '_' and '-'.
    /// </summary>
    public static bool IsValidClientId(string? id) => id is not null && ClientIdPattern().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex ClientIdPattern();
}
