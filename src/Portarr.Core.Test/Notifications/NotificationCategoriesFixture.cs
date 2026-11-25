using MafLabs.Notifications;
using Portarr.Core.Attention;
using Portarr.Core.Maintenance;
using Portarr.Core.Notifications;

namespace Portarr.Core.Test.Notifications;

public sealed class NotificationCategoriesFixture
{
    [Fact]
    public void should_declare_the_four_categories_with_their_defaults_and_maintenance_off()
    {
        var categories = TestCategories.Declared().All;

        categories.Select(c => c.Id).Should().Equal("apps", "infrastructure", "data", "maintenance");
        categories.ToDictionary(c => c.Id, c => (c.DefaultPolicy.Enabled, c.DefaultPolicy.MinimumLevel)).Should().BeEquivalentTo(new Dictionary<string, (bool, NotificationLevel)>
        {
            ["apps"] = (true, NotificationLevel.Danger),
            ["infrastructure"] = (true, NotificationLevel.Danger),
            ["data"] = (true, NotificationLevel.Warning),
            ["maintenance"] = (false, NotificationLevel.Danger),
        });
        categories.Should().OnlyContain(c => c.Title.Length > 0 && c.Description.Length > 0);
    }

    [Fact]
    public void should_be_quiet_by_default_because_little_closes_a_problem()
    {
        var categories = TestCategories.Declared().All;

        categories.Where(c => c.Id != "maintenance").Should().OnlyContain(c => c.DefaultPolicy.ReminderHours == 72 && c.DefaultPolicy.MaxReminders == 1 && c.DefaultPolicy.MaxPerDay == 2);
        categories.Single(c => c.Id == "maintenance").DefaultPolicy.Should().BeEquivalentTo(new CategoryPolicy { Enabled = false, MinimumLevel = NotificationLevel.Danger, ReminderHours = 168, MaxReminders = 0, MaxPerDay = 1 });
    }

    [Theory]
    [InlineData("apps", "apps")]
    [InlineData("infrastructure", "infrastructure")]
    [InlineData("data", "data")]
    [InlineData("maintenance", "maintenance")]
    [InlineData("catalog", null)]
    [InlineData("mcp", null)]
    [InlineData("anything-else", null)]
    public void should_map_the_area_of_a_finding_to_its_category_and_mail_nothing_for_the_others(string area, string? category)
    {
        PortarrNotificationCategories.CategoryOf(new AttentionFinding(area, AttentionLevel.Danger, "x", 1, [])).Should().Be(category);
    }

    [Theory]
    [InlineData(AttentionLevel.Danger, NotificationLevel.Danger)]
    [InlineData(AttentionLevel.Warning, NotificationLevel.Warning)]
    [InlineData(AttentionLevel.Info, NotificationLevel.Info)]
    public void should_map_the_level_of_a_finding_to_the_level_of_the_package(AttentionLevel level, NotificationLevel expected)
    {
        PortarrNotificationCategories.LevelOf(level).Should().Be(expected);
    }
}
