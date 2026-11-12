using FluentMigrator;

namespace Portarr.Core.Datastore.Migration;

/// <summary>
/// The inbox of captured MCP servers (V9, D-116): what an assistant handed over from a workstation, waiting for the operator to accept it
/// into the catalog or dismiss it. A capture is one hand-over (one workstation, client and scope); each of its servers is a row with its own
/// status, so the operator decides server by server. The payload is the assistant's description as JSON: names, addresses, commands and the
/// names of variables, never a secret value (the validator refuses those before anything is stored).
/// </summary>
[Migration(4, "mcp_captures")]
public sealed class McpCaptures : FluentMigrator.Migration
{
    public override void Up()
    {
        Create.Table("McpCaptures")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Workstation").AsString(64).NotNullable()
            .WithColumn("Client").AsString(32).NotNullable()
            .WithColumn("Scope").AsString(16).NotNullable()
            .WithColumn("ReceivedAt").AsString(40).NotNullable()
            .WithColumn("SubmittedBy").AsString(64).Nullable();

        Create.Table("McpCaptureServers")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("CaptureId").AsInt32().NotNullable().ForeignKey("McpCaptures", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("Identity").AsString(700).NotNullable()
            .WithColumn("Payload").AsString(20000).NotNullable()
            .WithColumn("Status").AsString(16).NotNullable()
            .WithColumn("ResolvedAt").AsString(40).Nullable()
            .WithColumn("ResolvedBy").AsString(64).Nullable()
            .WithColumn("ResolvedAs").AsString(64).Nullable();
        Create.Index("IX_McpCaptureServers_CaptureId").OnTable("McpCaptureServers").OnColumn("CaptureId").Ascending();
        Create.Index("IX_McpCaptureServers_Status").OnTable("McpCaptureServers").OnColumn("Status").Ascending();
    }

    public override void Down()
    {
        Delete.Table("McpCaptureServers");
        Delete.Table("McpCaptures");
    }
}
