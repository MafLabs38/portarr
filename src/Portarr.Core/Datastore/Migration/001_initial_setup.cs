using FluentMigrator;

namespace Portarr.Core.Datastore.Migration;

/// <summary>
/// Key-value settings (like the *arr apps' Config table). Migrations are never edited once
/// released: a schema change is a new migration.
/// </summary>
[Migration(1, "initial setup")]
public sealed class InitialSetup : FluentMigrator.Migration
{
    public override void Up()
    {
        Create.Table("Config")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Key").AsString(255).NotNullable().Unique()
            .WithColumn("Value").AsString(int.MaxValue).NotNullable();
    }

    public override void Down()
    {
        Delete.Table("Config");
    }
}
