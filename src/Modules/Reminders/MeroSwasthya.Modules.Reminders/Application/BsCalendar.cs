namespace MeroSwasthya.Modules.Reminders.Application;

/// <summary>
/// AD → Bikram Sambat, for the date in a Nepali message ("२०८३-०६-०२", A.2 Reminder example). The month
/// lengths are the table of the app's <c>nepali_utils</c> package, so the SMS and the app's screens
/// print the same date. Covers BS 2080–2100 (2023-04-14 … 2044-04-13); reminders are never scheduled
/// outside it, and a date that is falls back to the AD date.
/// </summary>
internal static class BsCalendar
{
    public const int FirstYear = 2080;

    /// <summary>1 Baishakh 2080.</summary>
    public static readonly DateOnly FirstDay = new(2023, 4, 14);

    private static readonly int[][] MonthDays =
    [
        [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30], // 2080
        [31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31], // 2081
        [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30], // 2082
        [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30], // 2083
        [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31], // 2084
        [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31], // 2085
        [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30], // 2086
        [31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30], // 2087
        [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31], // 2088
        [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31], // 2089
        [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30], // 2090
        [31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30], // 2091
        [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31], // 2092
        [31, 31, 31, 32, 31, 31, 29, 30, 29, 30, 29, 31], // 2093
        [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30], // 2094
        [31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30], // 2095
        [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31], // 2096
        [31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30], // 2097
        [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30], // 2098
        [31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30], // 2099
        [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31], // 2100
    ];

    /// <summary>The BS date of an AD calendar day, or null outside the table.</summary>
    public static (int Year, int Month, int Day)? FromAd(DateOnly ad)
    {
        var days = ad.DayNumber - FirstDay.DayNumber;
        if (days < 0) return null;

        for (var y = 0; y < MonthDays.Length; y++)
        {
            for (var m = 0; m < 12; m++)
            {
                if (days < MonthDays[y][m]) return (FirstYear + y, m + 1, days + 1);
                days -= MonthDays[y][m];
            }
        }
        return null;
    }

    /// <summary>"२०८३-०६-०२" for 2026-09-18.</summary>
    public static string Format(DateOnly ad) =>
        FromAd(ad) is (var year, var month, var day)
            ? NepaliDigits($"{year:D4}-{month:D2}-{day:D2}")
            : ad.ToString("yyyy-MM-dd");

    /// <summary>Latin digits → Devanagari digits; everything else unchanged.</summary>
    public static string NepaliDigits(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is >= '0' and <= '9' ? (char)('०' + (source[i] - '0')) : source[i];
        });
}
