using System.Globalization;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Shared.Paging;

/// <summary>A.1: pagination is cursor style (before / since / cursor), never page numbers.</summary>
public static class Cursors
{
    /// <summary>Parses <c>?limit=</c>; absent → <paramref name="defaultLimit"/>; outside 1..max → 400.</summary>
    public static int Limit(string? raw, int defaultLimit, int max, string field = "limit")
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultLimit;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit < 1 || limit > max)
            throw AppException.Validation(field, $"Must be an integer between 1 and {max}");
        return limit;
    }

    /// <summary>Parses an ISO-8601 cursor (<c>before</c>, <c>since</c>); absent → null; malformed → 400.</summary>
    public static DateTime? Timestamp(string? raw, string field)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!UtcDateTimeJsonConverter.TryParse(raw, out var value))
            throw AppException.Validation(field, "Must be an ISO-8601 UTC timestamp, e.g. 2026-09-18T04:00:00.000Z");
        return value;
    }

    public static string? ToWire(DateTime? at) => at is null ? null : UtcDateTimeJsonConverter.ToWire(at.Value);

    /// <summary>
    /// Keyset page over rows sorted by a timestamp descending: returns the page and the cursor for
    /// the next request (<c>null</c> when this is the last page).
    /// </summary>
    public static (IReadOnlyList<T> Page, DateTime? Next) PageDescending<T>(
        IEnumerable<T> items, Func<T, DateTime> at, DateTime? before, int limit)
    {
        var page = items
            .Where(i => before is null || at(i) < before.Value)
            .OrderByDescending(at)
            .Take(limit + 1)
            .ToList();
        if (page.Count <= limit) return (page, null);
        page.RemoveAt(page.Count - 1);
        return (page, at(page[^1]));
    }
}
