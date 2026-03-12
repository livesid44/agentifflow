using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskEmailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite does not support IF NOT EXISTS in ALTER TABLE ADD COLUMN.
                // Use the table-recreation pattern so this migration is idempotent
                // regardless of whether columns were previously added manually.
                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""AgentTasks_v2"" (
                        ""Id""                  INTEGER NOT NULL CONSTRAINT ""PK_AgentTasks"" PRIMARY KEY AUTOINCREMENT,
                        ""Title""               TEXT    NOT NULL DEFAULT '',
                        ""Description""         TEXT    NULL,
                        ""Status""              TEXT    NOT NULL DEFAULT 'Pending',
                        ""AssignedTo""          TEXT    NULL,
                        ""CreatedAt""           TEXT    NOT NULL DEFAULT '',
                        ""UpdatedAt""           TEXT    NULL,
                        ""CompletedAt""         TEXT    NULL,
                        ""Result""              TEXT    NULL,
                        ""SourceEmailId""       TEXT    NULL,
                        ""SourceEmailFrom""     TEXT    NULL,
                        ""SourceEmailSubject""  TEXT    NULL,
                        ""ConversationId""      TEXT    NULL
                    );
                ");

                migrationBuilder.Sql(@"
                    INSERT OR IGNORE INTO ""AgentTasks_v2""
                        (""Id"", ""Title"", ""Description"", ""Status"", ""AssignedTo"",
                         ""CreatedAt"", ""UpdatedAt"", ""CompletedAt"", ""Result"",
                         ""SourceEmailId"", ""SourceEmailFrom"", ""SourceEmailSubject"", ""ConversationId"")
                    SELECT ""Id"", ""Title"", ""Description"", ""Status"", ""AssignedTo"",
                           ""CreatedAt"", ""UpdatedAt"", ""CompletedAt"", ""Result"",
                           CASE WHEN (SELECT COUNT(*) FROM pragma_table_info('AgentTasks') WHERE name='SourceEmailId')      > 0 THEN ""SourceEmailId""      ELSE NULL END,
                           CASE WHEN (SELECT COUNT(*) FROM pragma_table_info('AgentTasks') WHERE name='SourceEmailFrom')    > 0 THEN ""SourceEmailFrom""    ELSE NULL END,
                           CASE WHEN (SELECT COUNT(*) FROM pragma_table_info('AgentTasks') WHERE name='SourceEmailSubject') > 0 THEN ""SourceEmailSubject"" ELSE NULL END,
                           CASE WHEN (SELECT COUNT(*) FROM pragma_table_info('AgentTasks') WHERE name='ConversationId')     > 0 THEN ""ConversationId""     ELSE NULL END
                    FROM ""AgentTasks"";
                ");

                migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AgentTasks"";");
                migrationBuilder.Sql(@"ALTER TABLE ""AgentTasks_v2"" RENAME TO ""AgentTasks"";");
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "SourceEmailId",
                    table: "AgentTasks",
                    type: "nvarchar(500)",
                    maxLength: 500,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "SourceEmailFrom",
                    table: "AgentTasks",
                    type: "nvarchar(300)",
                    maxLength: 300,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "SourceEmailSubject",
                    table: "AgentTasks",
                    type: "nvarchar(500)",
                    maxLength: 500,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "ConversationId",
                    table: "AgentTasks",
                    type: "nvarchar(500)",
                    maxLength: 500,
                    nullable: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite: reversing a table-recreation is complex; leave columns in place.
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropColumn(name: "SourceEmailId",      table: "AgentTasks");
                migrationBuilder.DropColumn(name: "SourceEmailFrom",    table: "AgentTasks");
                migrationBuilder.DropColumn(name: "SourceEmailSubject", table: "AgentTasks");
                migrationBuilder.DropColumn(name: "ConversationId",     table: "AgentTasks");
            }
        }
    }
}
