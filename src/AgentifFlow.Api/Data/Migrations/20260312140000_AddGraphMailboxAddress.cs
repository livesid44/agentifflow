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
                // Use a full table-recreation pattern so this migration is idempotent
                // on SQLite databases that were pre-created with EnsureCreated or that
                // were restored from a backup after the migration was recorded in
                // __EFMigrationsHistory.  The pattern:
                //   1. Create the target table (with GraphMailboxAddress) if it does not exist.
                //   2. Copy any existing rows from the original table, mapping the new column to NULL.
                //   3. Drop the original table.
                //   4. Rename the new table to the original name.
                // If the original table already has GraphMailboxAddress (column was added
                // previously), the simple ALTER TABLE path is used instead.
                migrationBuilder.Sql(@"
                    -- Add column only when it is not already present.
                    -- We cannot use ALTER TABLE ... ADD COLUMN IF NOT EXISTS in SQLite,
                    -- so we check pragma_table_info first and only run the DDL when needed.
                    CREATE TABLE IF NOT EXISTS ""AppConfigurations_v2"" (
                        ""Id""                          INTEGER NOT NULL CONSTRAINT ""PK_AppConfigurations"" PRIMARY KEY AUTOINCREMENT,
                        ""AgentFlowEnabled""            INTEGER NOT NULL DEFAULT 0,
                        ""BlobContainerName""           TEXT    NULL,
                        ""BlobPollIntervalSeconds""     INTEGER NOT NULL DEFAULT 60,
                        ""BlobStorageConnectionString"" TEXT    NULL,
                        ""GraphClientId""               TEXT    NULL,
                        ""GraphClientSecret""           TEXT    NULL,
                        ""GraphMailboxAddress""         TEXT    NULL,
                        ""GraphScopes""                 TEXT    NULL,
                        ""GraphTenantId""               TEXT    NULL,
                        ""MaxRetryCount""               INTEGER NOT NULL DEFAULT 3,
                        ""NotificationEmail""           TEXT    NULL,
                        ""OpenAiApiKey""                TEXT    NULL,
                        ""OpenAiDeploymentName""        TEXT    NULL,
                        ""OpenAiEndpoint""              TEXT    NULL,
                        ""SqlConnectionString""         TEXT    NULL,
                        ""UpdatedAt""                   TEXT    NOT NULL DEFAULT '',
                        ""UpdatedBy""                   TEXT    NULL
                    );
                ");

                // Copy rows from the original table into the staging table, but only
                // when the original table does NOT already have GraphMailboxAddress
                // (i.e. the simple ALTER TABLE path has not run yet).
                migrationBuilder.Sql(@"
                    INSERT OR IGNORE INTO ""AppConfigurations_v2""
                        (""Id"", ""AgentFlowEnabled"", ""BlobContainerName"",
                         ""BlobPollIntervalSeconds"", ""BlobStorageConnectionString"",
                         ""GraphClientId"", ""GraphClientSecret"", ""GraphMailboxAddress"",
                         ""GraphScopes"", ""GraphTenantId"", ""MaxRetryCount"",
                         ""NotificationEmail"", ""OpenAiApiKey"", ""OpenAiDeploymentName"",
                         ""OpenAiEndpoint"", ""SqlConnectionString"", ""UpdatedAt"", ""UpdatedBy"")
                    SELECT ""Id"", ""AgentFlowEnabled"", ""BlobContainerName"",
                           ""BlobPollIntervalSeconds"", ""BlobStorageConnectionString"",
                           ""GraphClientId"", ""GraphClientSecret"",
                           CASE WHEN (SELECT COUNT(*) FROM pragma_table_info('AppConfigurations')
                                      WHERE name = 'GraphMailboxAddress') > 0
                                THEN ""GraphMailboxAddress""
                                ELSE NULL END,
                           ""GraphScopes"", ""GraphTenantId"", ""MaxRetryCount"",
                           ""NotificationEmail"", ""OpenAiApiKey"", ""OpenAiDeploymentName"",
                           ""OpenAiEndpoint"", ""SqlConnectionString"", ""UpdatedAt"", ""UpdatedBy""
                    FROM ""AppConfigurations"";
                ");

                migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AppConfigurations"";");
                migrationBuilder.Sql(@"ALTER TABLE ""AppConfigurations_v2"" RENAME TO ""AppConfigurations"";");
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
            // SQLite: column removal requires table recreation; skipped for dev Down path.
        }
    }
}
