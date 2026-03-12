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
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // Use IF NOT EXISTS so the migration is idempotent for databases that were
                // previously created with EnsureCreated before migrations were introduced.
                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""AppConfigurations"" (
                        ""Id""                         INTEGER NOT NULL CONSTRAINT ""PK_AppConfigurations"" PRIMARY KEY AUTOINCREMENT,
                        ""GraphTenantId""              TEXT    NULL,
                        ""GraphClientId""              TEXT    NULL,
                        ""GraphClientSecret""          TEXT    NULL,
                        ""GraphScopes""                TEXT    NULL,
                        ""OpenAiEndpoint""             TEXT    NULL,
                        ""OpenAiApiKey""               TEXT    NULL,
                        ""OpenAiDeploymentName""       TEXT    NULL,
                        ""BlobStorageConnectionString"" TEXT   NULL,
                        ""BlobContainerName""          TEXT    NULL,
                        ""SqlConnectionString""        TEXT    NULL,
                        ""UpdatedAt""                  TEXT    NOT NULL,
                        ""UpdatedBy""                  TEXT    NULL
                    );
                ");
            }
            else
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConfigurations");
        }
    }
}
