using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoRetryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite does not support ALTER TABLE ADD COLUMN for columns with
                // non-constant defaults, so we add columns individually using
                // EnsureSqliteColumnAsync at startup.  The migration body here is a
                // no-op for SQLite; the safety-net in Program.cs handles it.
            }
            else
            {
                migrationBuilder.AddColumn<int>(
                    name: "AutoRetryIntervalMinutes",
                    table: "AppConfigurations",
                    type: "int",
                    nullable: false,
                    defaultValue: 30);

                migrationBuilder.AddColumn<DateTime>(
                    name: "RetryAfterUtc",
                    table: "BlobWatcherJobs",
                    type: "datetime2",
                    nullable: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropColumn(name: "AutoRetryIntervalMinutes", table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "RetryAfterUtc",            table: "BlobWatcherJobs");
            }
        }
    }
}
