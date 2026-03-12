using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // Use IF NOT EXISTS so the migration is idempotent for databases that were
                // previously created with EnsureCreated before migrations were introduced.
                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""AgentTasks"" (
                        ""Id""          INTEGER  NOT NULL CONSTRAINT ""PK_AgentTasks"" PRIMARY KEY AUTOINCREMENT,
                        ""Title""       TEXT     NOT NULL,
                        ""Description"" TEXT     NULL,
                        ""Status""      TEXT     NOT NULL,
                        ""AssignedTo""  TEXT     NULL,
                        ""CreatedAt""   TEXT     NOT NULL,
                        ""UpdatedAt""   TEXT     NULL,
                        ""CompletedAt"" TEXT     NULL,
                        ""Result""      TEXT     NULL
                    );
                ");
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "AgentTasks",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "int", nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                        Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                        Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                        AssignedTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                        CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                        UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                        CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                        Result = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AgentTasks", x => x.Id);
                    });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTasks");
        }
    }
}
