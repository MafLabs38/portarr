using Portarr.Core.Data;

namespace Portarr.Core.Test.Data;

/// <summary>The next firing of a cron expression, as backup tools write theirs (UTC).</summary>
public sealed class CronScheduleFixture
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0 3 * * *", "2026-02-01T03:00:00Z")]
    [InlineData("45 12 * * *", "2026-01-31T12:45:00Z")]
    [InlineData("30 12 * * *", "2026-02-01T12:30:00Z")]
    [InlineData("*/15 * * * *", "2026-01-31T12:45:00Z")]
    [InlineData("0 */6 * * *", "2026-01-31T18:00:00Z")]
    [InlineData("0 1,13 * * *", "2026-01-31T13:00:00Z")]
    [InlineData("0 9-11 * * *", "2026-02-01T09:00:00Z")]
    [InlineData("5/20 14 * * *", "2026-01-31T14:05:00Z")]
    [InlineData("0 0 1 * *", "2026-02-01T00:00:00Z")]
    [InlineData("0 0 * * 1", "2026-02-02T00:00:00Z")]
    [InlineData("0 0 * * mon", "2026-02-02T00:00:00Z")]
    [InlineData("0 0 * * 7", "2026-02-01T00:00:00Z")]
    [InlineData("0 0 15 mar *", "2026-03-15T00:00:00Z")]
    [InlineData("@hourly", "2026-01-31T13:00:00Z")]
    [InlineData("@daily", "2026-02-01T00:00:00Z")]
    [InlineData("@weekly", "2026-02-01T00:00:00Z")]
    [InlineData("@monthly", "2026-02-01T00:00:00Z")]
    [InlineData("@yearly", "2027-01-01T00:00:00Z")]
    public void should_find_the_next_firing(string expression, string expected)
    {
        CronSchedule.NextAfter(expression, Now).Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void should_fire_on_the_first_of_the_month_when_it_comes_before_the_monday()
    {
        // The 1st of the month or a Monday: the 1st of February is a Sunday, before Monday the 2nd.
        CronSchedule.NextAfter("0 0 1 2 1", Now).Should().Be(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_fire_on_the_monday_when_it_comes_before_the_day_of_the_month()
    {
        CronSchedule.NextAfter("0 0 3 2 1", Now).Should().Be(new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_find_the_29th_of_february_in_the_next_leap_year()
    {
        CronSchedule.NextAfter("0 0 29 2 *", Now).Should().Be(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2026-01-31T12:29:59Z", "2026-01-31T12:30:00Z")]
    [InlineData("2026-01-31T12:30:00Z", "2026-02-01T12:30:00Z")]
    [InlineData("2026-01-31T12:30:01Z", "2026-02-01T12:30:00Z")]
    public void should_find_a_firing_strictly_after_the_moment_asked_not_at_it(string after, string expected)
    {
        CronSchedule.NextAfter("30 12 * * *", DateTimeOffset.Parse(after, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0 3 * * *", "Europe/Paris", "2026-02-01T02:00:00Z")]
    [InlineData("0 3 * * *", "UTC", "2026-02-01T03:00:00Z")]
    [InlineData("0 3 * * *", "America/New_York", "2026-02-01T08:00:00Z")]
    public void should_read_the_expression_in_the_zone_the_tool_names(string expression, string zone, string expected)
    {
        var next = CronSchedule.NextAfter(expression, Now, TimeZoneInfo.FindSystemTimeZoneById(zone));

        next.Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void should_run_a_job_scheduled_in_the_hour_daylight_saving_skips_just_after_the_gap()
    {
        // 2026-03-29 02:30 does not exist in Paris (clocks go from 02:00 to 03:00): the job runs at 03:30 local, 01:30 UTC.
        var next = CronSchedule.NextAfter("30 2 29 3 *", new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));

        next.Should().Be(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_read_the_time_in_utc()
    {
        var local = new DateTimeOffset(2026, 1, 31, 23, 30, 0, TimeSpan.FromHours(2));

        CronSchedule.NextAfter("0 22 * * *", local).Should().Be(new DateTimeOffset(2026, 1, 31, 22, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("disabled")]
    [InlineData("every 2 d")]
    [InlineData("0 3 * *")]
    [InlineData("0 3 * * * *")]
    [InlineData("61 3 * * *")]
    [InlineData("0 24 * * *")]
    [InlineData("0 0 0 * *")]
    [InlineData("0 0 * 13 *")]
    [InlineData("0 0 * * 8")]
    [InlineData("*/0 * * * *")]
    [InlineData("a-b * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("1-2-3 * * * *")]
    [InlineData("0 0 * * foo")]
    public void should_have_no_next_firing_for_text_that_is_not_a_cron_expression(string? expression)
    {
        CronSchedule.NextAfter(expression, Now).Should().BeNull();
    }

    [Fact]
    public void should_give_up_on_a_date_that_never_comes()
    {
        CronSchedule.NextAfter("0 0 31 2 *", Now).Should().BeNull("the 31st of February does not exist");
    }
}
