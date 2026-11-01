using System.Globalization;

namespace Portarr.Core.Data;

/// <summary>
/// The next time a standard five-field cron expression fires (minute, hour, day of month, month, day of week),
/// read in UTC. Backup tools expose their cron but not the next time: the Data page uses it to tell "waiting for
/// its first run" from "never going to run". Supports lists, ranges, steps, month and weekday names and the
/// <c>@hourly</c> to <c>@yearly</c> shortcuts; anything else (<c>every 2 d</c>, <c>disabled</c>) has no next time.
/// </summary>
public static class CronSchedule
{
    private const int SearchDays = 366 * 5;

    private static readonly Dictionary<string, string> Shortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@yearly"] = "0 0 1 1 *",
        ["@annually"] = "0 0 1 1 *",
        ["@monthly"] = "0 0 1 * *",
        ["@weekly"] = "0 0 * * 0",
        ["@daily"] = "0 0 * * *",
        ["@midnight"] = "0 0 * * *",
        ["@hourly"] = "0 * * * *",
    };

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>
    /// The first firing strictly after <paramref name="after"/> when the expression is read in <paramref name="zone"/>: the
    /// clock of that zone decides the fields, the answer is in UTC. A time that does not exist that day (the hour skipped by
    /// daylight saving) runs just after the gap.
    /// </summary>
    public static DateTimeOffset? NextAfter(string? expression, DateTimeOffset after, TimeZoneInfo zone)
    {
        var wall = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        if (NextAfter(expression, new DateTimeOffset(wall, TimeSpan.Zero)) is not { } next)
        {
            return null;
        }

        var firing = DateTime.SpecifyKind(next.DateTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(firing))
        {
            firing = firing.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(firing, zone), TimeSpan.Zero);
    }

    /// <summary>The first firing strictly after <paramref name="after"/>, in UTC, or null when the text is not a cron expression.</summary>
    public static DateTimeOffset? NextAfter(string? expression, DateTimeOffset after)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var text = Shortcuts.GetValueOrDefault(expression.Trim(), expression.Trim());
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            return null;
        }

        var minutes = Parse(fields[0], 0, 59, null);
        var hours = Parse(fields[1], 0, 23, null);
        var daysOfMonth = Parse(fields[2], 1, 31, null);
        var months = Parse(fields[3], 1, 12, Months);
        var daysOfWeek = Parse(fields[4], 0, 7, Days);
        if (minutes is null || hours is null || daysOfMonth is null || months is null || daysOfWeek is null)
        {
            return null;
        }

        // Sunday is 0 and 7.
        if (daysOfWeek.Contains(7))
        {
            daysOfWeek.Add(0);
        }

        var anyDayOfMonth = daysOfMonth.Count == 31;
        var anyDayOfWeek = daysOfWeek.Count >= 7;
        var start = after.ToUniversalTime();
        var day = new DateTimeOffset(start.Year, start.Month, start.Day, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < SearchDays; i++, day = day.AddDays(1))
        {
            if (months.Contains(day.Month) && DayMatches(day, daysOfMonth, daysOfWeek, anyDayOfMonth, anyDayOfWeek)
                && FirstTimeAfter(day, hours, minutes, start) is { } next)
            {
                return next;
            }
        }

        return null;
    }

    // Like cron: when both day fields are restricted, either one matching is enough.
    private static bool DayMatches(DateTimeOffset day, HashSet<int> daysOfMonth, HashSet<int> daysOfWeek, bool anyDayOfMonth, bool anyDayOfWeek)
    {
        var ofMonth = daysOfMonth.Contains(day.Day);
        var ofWeek = daysOfWeek.Contains((int)day.DayOfWeek);
        return anyDayOfMonth || anyDayOfWeek ? ofMonth && ofWeek : ofMonth || ofWeek;
    }

    private static DateTimeOffset? FirstTimeAfter(DateTimeOffset day, HashSet<int> hours, HashSet<int> minutes, DateTimeOffset after)
    {
        foreach (var hour in hours.Order())
        {
            foreach (var minute in minutes.Order())
            {
                var candidate = day.AddHours(hour).AddMinutes(minute);
                if (candidate > after)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static HashSet<int>? Parse(string field, int min, int max, string[]? names)
    {
        var values = new HashSet<int>();
        return field.Split(',').Any(part => !AddPart(values, part, min, max, names)) ? null : values;
    }

    private static bool AddPart(HashSet<int> values, string part, int min, int max, string[]? names)
    {
        var step = 1;
        var range = part;
        var slash = part.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            if (!TryStep(part[(slash + 1)..], out step))
            {
                return false;
            }

            range = part[..slash];
        }

        if (!TryRange(range, slash >= 0, min, max, names, out var from, out var to) || to < from)
        {
            return false;
        }

        for (var value = from; value <= to; value += step)
        {
            values.Add(value);
        }

        return true;
    }

    private static bool TryStep(string text, out int step) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out step) && step >= 1;

    /// <summary>The bounds of a field part without its step: <c>*</c> is all, <c>a-b</c> a range, <c>a</c> alone one value (or from a to the end when a step follows).</summary>
    private static bool TryRange(string range, bool hasStep, int min, int max, string[]? names, out int from, out int to)
    {
        (from, to) = (min, max);
        if (range is "*" or "?")
        {
            return true;
        }

        var bounds = range.Split('-');
        if (bounds.Length > 2 || !TryValue(bounds[0], min, max, names, out from))
        {
            return false;
        }

        to = from;
        if (bounds.Length == 2)
        {
            return TryValue(bounds[1], min, max, names, out to);
        }

        // "5/10" means from 5 to the end.
        if (hasStep)
        {
            to = max;
        }

        return true;
    }

    private static bool TryValue(string text, int min, int max, string[]? names, out int value)
    {
        var index = names is null ? -1 : Array.FindIndex(names, n => n.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            value = min == 0 ? index : index + 1;
            return true;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
    }
}
