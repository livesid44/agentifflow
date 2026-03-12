using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGraphMailboxAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite does not support adding columns with certain constraints in ALTER TABLE,
                // but a simple nullable column is fine.
                migrationBuilder.Sql(@"
                    ALTER TABLE ""AppConfigurations""
                    ADD COLUMN ""GraphMailboxAddress"" TEXT NULL;
                ");
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "GraphMailboxAddress",
                    table: "AppConfigurations",
                    type: "nvarchar(300)",
                    maxLength: 300,
                    nullable: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropColumn(
                    name: "GraphMailboxAddress",
                    table: "AppConfigurations");
            }
            // SQLite does not support DROP COLUMN in older versions; skipped for dev.
        }
    }
}
