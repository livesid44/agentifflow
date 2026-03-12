using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentFlowAndBlobJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── New agent-flow columns on AppConfigurations ───────────────────
            migrationBuilder.AddColumn<string>(
                name: "NotificationEmail",
                table: "AppConfigurations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlobPollIntervalSeconds",
                table: "AppConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetryCount",
                table: "AppConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "AgentFlowEnabled",
                table: "AppConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // ── BlobWatcherJobs table ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "BlobWatcherJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlobName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContainerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LogDetails = table.Column<string>(nullable: true),
                    RowsInserted = table.Column<int>(type: "int", nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlobWatcherJobs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BlobWatcherJobs");

            migrationBuilder.DropColumn(name: "AgentFlowEnabled",       table: "AppConfigurations");
            migrationBuilder.DropColumn(name: "BlobPollIntervalSeconds", table: "AppConfigurations");
            migrationBuilder.DropColumn(name: "MaxRetryCount",           table: "AppConfigurations");
            migrationBuilder.DropColumn(name: "NotificationEmail",       table: "AppConfigurations");
        }
    }
}
