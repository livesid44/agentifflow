using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <summary>
    /// Adds the AgentSkills junction table.
    /// Each row links an Agent to a SkillType (EmailMonitoring | FileMonitoring |
    /// DataValidation | SqlManagement) with an IsEnabled flag and optional JSON config.
    /// </summary>
    public partial class AddAgentSkills : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""AgentSkills"" (
                        ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""AgentId""    INTEGER NOT NULL,
                        ""SkillType""  TEXT    NOT NULL DEFAULT '',
                        ""IsEnabled""  INTEGER NOT NULL DEFAULT 1,
                        ""ConfigJson"" TEXT    NULL,
                        CONSTRAINT ""FK_AgentSkills_Agents"" FOREIGN KEY (""AgentId"")
                            REFERENCES ""Agents"" (""Id"") ON DELETE CASCADE
                    );
                ");
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "AgentSkills",
                    columns: table => new
                    {
                        Id         = table.Column<int>(type: "int", nullable: false)
                                          .Annotation("SqlServer:Identity", "1, 1"),
                        AgentId    = table.Column<int>(type: "int", nullable: false),
                        SkillType  = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                        IsEnabled  = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AgentSkills", x => x.Id);
                        table.ForeignKey(
                            name: "FK_AgentSkills_Agents_AgentId",
                            column: x => x.AgentId,
                            principalTable: "Agents",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_AgentSkills_AgentId",
                    table: "AgentSkills",
                    column: "AgentId");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
                migrationBuilder.DropTable(name: "AgentSkills");
        }
    }
}
