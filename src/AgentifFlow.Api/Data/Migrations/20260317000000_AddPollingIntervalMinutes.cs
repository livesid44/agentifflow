using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPollingIntervalMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite — add column idempotently via table recreation pattern.
                migrationBuilder.Sql(@"
                    ALTER TABLE ""Agents"" ADD COLUMN ""PollingIntervalMinutes"" INTEGER NOT NULL DEFAULT 5;
                ");
            }
            else
            {
                migrationBuilder.AddColumn<int>(
                    name: "PollingIntervalMinutes",
                    table: "Agents",
                    type: "int",
                    nullable: false,
                    defaultValue: 5);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropColumn(
                    name: "PollingIntervalMinutes",
                    table: "Agents");
            }
        }
    }
}
