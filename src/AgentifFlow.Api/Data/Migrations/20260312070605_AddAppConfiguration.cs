using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GraphTenantId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GraphClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GraphClientSecret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GraphScopes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OpenAiEndpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OpenAiApiKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OpenAiDeploymentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BlobStorageConnectionString = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BlobContainerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SqlConnectionString = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigurations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConfigurations");
        }
    }
}
