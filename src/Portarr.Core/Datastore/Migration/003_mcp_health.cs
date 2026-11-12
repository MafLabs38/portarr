using FluentMigrator;

namespace Portarr.Core.Datastore.Migration;

/// <summary>
/// The health of the MCP servers the catalog declares (D-114): the current state, since when, and when the server
/// was last seen healthy, so that "down for 12 days" survives a restart. One row per server, replaced at each check;
/// the history of results stays in memory like every check (D-51). Timestamps are ISO 8601 text, as everywhere.
/// </summary>
[Migration(3, "mcp_health")]
public sealed class McpHealth : FluentMigrator.Migration
{
    public override void Up()
    {
        Create.Table("McpServerHealth")
            .WithColumn("ServerId").AsString(64).PrimaryKey()
            .WithColumn("State").AsString(32).NotNullable()
            .WithColumn("Since").AsString(40).NotNullable()
            .WithColumn("LastHealthyAt").AsString(40).Nullable()
            .WithColumn("LastCheckedAt").AsString(40).NotNullable()
            .WithColumn("Error").AsString(64).Nullable()
            .WithColumn("StatusCode").AsInt32().Nullable();
    }

    public override void Down()
    {
        Delete.Table("McpServerHealth");
    }
}
