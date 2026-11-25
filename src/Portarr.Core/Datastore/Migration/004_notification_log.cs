using FluentMigrator;

namespace Portarr.Core.Datastore.Migration;

/// <summary>
/// The mails Portarr tried to send and the categories it is writing about (D-129, ADR 0022). <c>NotificationLog</c> holds one row per attempt,
/// delivered or not (<c>Kind</c> is <c>category</c> or <c>test</c>, <c>Category</c> the id of the category, <c>Level</c> the level of the mail), so that the
/// daily caps survive a restart and the page can say why a mail did not leave (never the server's text, never a secret). <c>NotificationEpisode</c> holds one
/// row per category that is going on (its id is the key): when it began, when it was last seen, how many mails it got, so that a restart sends nothing twice
/// and a category that clears writes nothing. Timestamps are ISO 8601 text, as everywhere.
/// </summary>
[Migration(4, "notification_log")]
public sealed class NotificationLog : FluentMigrator.Migration
{
    public override void Up()
    {
        Create.Table("NotificationLog")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("Kind").AsString(16).NotNullable()
            .WithColumn("Category").AsString(40).Nullable()
            .WithColumn("EpisodeKey").AsString(80).Nullable()
            .WithColumn("Level").AsString(16).Nullable()
            .WithColumn("Reminder").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("SentAt").AsString(40).NotNullable()
            .WithColumn("Delivered").AsBoolean().NotNullable()
            .WithColumn("Error").AsString(200).Nullable();
        Create.Index("IX_NotificationLog_SentAt").OnTable("NotificationLog").OnColumn("SentAt").Ascending();

        Create.Table("NotificationEpisode")
            .WithColumn("EpisodeKey").AsString(40).PrimaryKey()
            .WithColumn("StartedAt").AsString(40).NotNullable()
            .WithColumn("LastSeenAt").AsString(40).NotNullable()
            .WithColumn("Mails").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LastMailedAt").AsString(40).Nullable()
            .WithColumn("LastAttemptAt").AsString(40).Nullable();
    }

    public override void Down()
    {
        Delete.Table("NotificationEpisode");
        Delete.Table("NotificationLog");
    }
}
