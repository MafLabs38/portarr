using System.Text.Json.Nodes;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Core.Test.Catalog;

/// <summary>Approving a change applies it key by key over the item as it is now: what was edited meanwhile is kept, a clash is refused.</summary>
public sealed class PendingMergeFixture
{
    private static JsonObject Item(string name, string? url = null)
    {
        var item = new JsonObject { ["id"] = "sonarr", ["name"] = name };
        if (url is not null)
        {
            item["check"] = new JsonObject { ["url"] = url };
        }

        return item;
    }

    [Fact]
    public void should_add_an_item_nobody_added_meanwhile()
    {
        PendingMerge.Apply("apps", "sonarr", null, Item("Sonarr"), null)!["name"]!.ToString().Should().Be("Sonarr");
    }

    [Fact]
    public void should_refuse_to_add_an_item_that_appeared_meanwhile()
    {
        var act = () => PendingMerge.Apply("apps", "sonarr", null, Item("Sonarr"), Item("Other"));

        act.Should().Throw<CatalogEditException>().Which.Status.Should().Be(CatalogEditStatus.Conflict);
    }

    [Fact]
    public void should_remove_an_item_that_is_unchanged()
    {
        PendingMerge.Apply("apps", "sonarr", Item("Sonarr"), null, Item("Sonarr")).Should().BeNull();
    }

    [Fact]
    public void should_refuse_to_remove_an_item_edited_meanwhile()
    {
        var act = () => PendingMerge.Apply("apps", "sonarr", Item("Sonarr"), null, Item("Sonarr TV"));

        act.Should().Throw<CatalogEditException>().Which.Status.Should().Be(CatalogEditStatus.Conflict);
    }

    [Fact]
    public void should_refuse_a_change_to_an_item_that_vanished()
    {
        var act = () => PendingMerge.Apply("apps", "sonarr", Item("Sonarr"), Item("Sonarr", "http://10.0.0.7/"), null);

        act.Should().Throw<CatalogEditException>().Which.Status.Should().Be(CatalogEditStatus.Conflict);
    }

    [Fact]
    public void should_accept_a_key_somebody_already_set_to_the_requested_value()
    {
        var merged = PendingMerge.Apply("apps", "sonarr", Item("Sonarr"), Item("Sonarr", "http://10.0.0.7/"), Item("Sonarr", "http://10.0.0.7/"))!;

        merged["check"]!["url"]!.ToString().Should().Be("http://10.0.0.7/");
    }

    [Fact]
    public void should_refuse_a_change_of_two_keys_when_only_one_of_them_clashes()
    {
        var before = Item("Sonarr", "http://10.0.0.7/");
        var after = Item("Sonarr TV", "http://10.0.0.8/");
        var now = Item("Sonarr", "http://10.0.0.9/");

        var act = () => PendingMerge.Apply("apps", "sonarr", before, after, now);

        act.Should().Throw<CatalogEditException>().Which.Message.Should().Contain("check");
    }

    [Fact]
    public void should_apply_a_removed_key_and_keep_the_other_edits()
    {
        var merged = PendingMerge.Apply("apps", "sonarr", Item("Sonarr", "http://10.0.0.7/"), Item("Sonarr"), Item("Sonarr TV", "http://10.0.0.7/"))!;

        merged.ContainsKey("check").Should().BeFalse("the change removed it");
        merged["name"]!.ToString().Should().Be("Sonarr TV", "the name edited meanwhile is kept");
    }
}
