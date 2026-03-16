using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite — create new tables idempotently.
                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""Agents"" (
                        ""Id""                       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""Name""                     TEXT    NOT NULL DEFAULT '',
                        ""Description""              TEXT    NULL,
                        ""IsEnabled""                INTEGER NOT NULL DEFAULT 1,
                        ""BlobContainerName""        TEXT    NULL,
                        ""NotificationEmail""        TEXT    NULL,
                        ""NotifyOnSuccess""          INTEGER NOT NULL DEFAULT 0,
                        ""NotifyOnFileNotFound""     INTEGER NOT NULL DEFAULT 1,
                        ""NotifyOnDataIssue""        INTEGER NOT NULL DEFAULT 1,
                        ""MaxRetryCount""            INTEGER NOT NULL DEFAULT 3,
                        ""AutoRetryIntervalMinutes"" INTEGER NOT NULL DEFAULT 30,
                        ""SqlPushEnabled""           INTEGER NOT NULL DEFAULT 0,
                        ""SqlTargetTable""           TEXT    NULL,
                        ""SqlColumnMappingJson""     TEXT    NULL,
                        ""BlobArchiveFilePattern""   TEXT    NULL,
                        ""BlobArchiveAppendDate""    INTEGER NOT NULL DEFAULT 1,
                        ""CreatedAt""                TEXT    NOT NULL DEFAULT '',
                        ""UpdatedAt""                TEXT    NOT NULL DEFAULT ''
                    );
                ");

                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""AgentFileTargets"" (
                        ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""AgentId""     INTEGER NOT NULL,
                        ""FilePattern"" TEXT    NOT NULL DEFAULT '',
                        ""AppendDate""  INTEGER NOT NULL DEFAULT 1,
                        ""IsRequired""  INTEGER NOT NULL DEFAULT 1,
                        CONSTRAINT ""FK_AgentFileTargets_Agents"" FOREIGN KEY (""AgentId"")
                            REFERENCES ""Agents"" (""Id"") ON DELETE CASCADE
                    );
                ");

                // BlobWatcherJobs.AgentId — handled by Program.cs safety-net.
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "Agents",
                    columns: table => new
                    {
                        Id                       = table.Column<int>(type: "int", nullable: false)
                                                         .Annotation("SqlServer:Identity", "1, 1"),
                        Name                     = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                        Description              = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                        IsEnabled                = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        BlobContainerName        = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                        NotificationEmail        = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                        NotifyOnSuccess          = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                        NotifyOnFileNotFound     = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        NotifyOnDataIssue        = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        MaxRetryCount            = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                        AutoRetryIntervalMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                        SqlPushEnabled           = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                        SqlTargetTable           = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                        SqlColumnMappingJson     = table.Column<string>(type: "nvarchar(max)", nullable: true),
                        BlobArchiveFilePattern   = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                        BlobArchiveAppendDate    = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        CreatedAt                = table.Column<DateTime>(type: "datetime2", nullable: false),
                        UpdatedAt                = table.Column<DateTime>(type: "datetime2", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Agents", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "AgentFileTargets",
                    columns: table => new
                    {
                        Id          = table.Column<int>(type: "int", nullable: false)
                                           .Annotation("SqlServer:Identity", "1, 1"),
                        AgentId     = table.Column<int>(type: "int", nullable: false),
                        FilePattern = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                        AppendDate  = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        IsRequired  = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AgentFileTargets", x => x.Id);
                        table.ForeignKey(
                            name: "FK_AgentFileTargets_Agents_AgentId",
                            column: x => x.AgentId,
                            principalTable: "Agents",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.AddColumn<int>(
                    name: "AgentId",
                    table: "BlobWatcherJobs",
                    type: "int",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: "IX_AgentFileTargets_AgentId",
                    table: "AgentFileTargets",
                    column: "AgentId");

                migrationBuilder.AddForeignKey(
                    name: "FK_BlobWatcherJobs_Agents_AgentId",
                    table: "BlobWatcherJobs",
                    column: "AgentId",
                    principalTable: "Agents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropForeignKey(name: "FK_BlobWatcherJobs_Agents_AgentId", table: "BlobWatcherJobs");
                migrationBuilder.DropColumn(name: "AgentId", table: "BlobWatcherJobs");
                migrationBuilder.DropTable(name: "AgentFileTargets");
                migrationBuilder.DropTable(name: "Agents");
            }
        }
    }
}
