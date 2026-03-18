using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentDesignerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // Table-recreation pattern for SQLite idempotency.
                // Creates a new table with all existing + new columns, copies data, swaps names.
                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""AppConfigurations_agentdesigner"" (
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
                        ""UpdatedAt""                   TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                        ""UpdatedBy""                   TEXT    NULL,
                        -- Agent Designer columns
                        ""BlobEnabled""                 INTEGER NOT NULL DEFAULT 0,
                        ""BlobReadEmailId""             TEXT    NULL,
                        ""BlobReadEmailAppendDate""     INTEGER NOT NULL DEFAULT 0,
                        ""BlobInputFilePattern""        TEXT    NULL,
                        ""BlobInputAppendDate""         INTEGER NOT NULL DEFAULT 1,
                        ""BlobArchiveFilePattern""      TEXT    NULL,
                        ""BlobArchiveAppendDate""       INTEGER NOT NULL DEFAULT 1,
                        ""NotifyOnSuccess""             INTEGER NOT NULL DEFAULT 0,
                        ""NotifyOnFileNotFound""        INTEGER NOT NULL DEFAULT 1,
                        ""NotifyOnDataIssue""           INTEGER NOT NULL DEFAULT 1,
                        ""SqlPushEnabled""              INTEGER NOT NULL DEFAULT 0,
                        ""SqlTargetTable""              TEXT    NULL,
                        ""SqlColumnMappingJson""        TEXT    NULL
                    );

                    INSERT OR IGNORE INTO ""AppConfigurations_agentdesigner"" (
                        ""Id"", ""AgentFlowEnabled"", ""BlobContainerName"",
                        ""BlobPollIntervalSeconds"", ""BlobStorageConnectionString"",
                        ""GraphClientId"", ""GraphClientSecret"", ""GraphMailboxAddress"",
                        ""GraphScopes"", ""GraphTenantId"", ""MaxRetryCount"",
                        ""NotificationEmail"", ""OpenAiApiKey"", ""OpenAiDeploymentName"",
                        ""OpenAiEndpoint"", ""SqlConnectionString"", ""UpdatedAt"", ""UpdatedBy""
                    )
                    SELECT
                        ""Id"", ""AgentFlowEnabled"", ""BlobContainerName"",
                        ""BlobPollIntervalSeconds"", ""BlobStorageConnectionString"",
                        ""GraphClientId"", ""GraphClientSecret"", ""GraphMailboxAddress"",
                        ""GraphScopes"", ""GraphTenantId"", ""MaxRetryCount"",
                        ""NotificationEmail"", ""OpenAiApiKey"", ""OpenAiDeploymentName"",
                        ""OpenAiEndpoint"", ""SqlConnectionString"", ""UpdatedAt"", ""UpdatedBy""
                    FROM ""AppConfigurations"";

                    DROP TABLE IF EXISTS ""AppConfigurations"";
                    ALTER TABLE ""AppConfigurations_agentdesigner"" RENAME TO ""AppConfigurations"";
                ");
            }
            else
            {
                migrationBuilder.AddColumn<bool>(
                    name: "BlobEnabled",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: false);
                migrationBuilder.AddColumn<string>(
                    name: "BlobReadEmailId",
                    table: "AppConfigurations",
                    type: "nvarchar(300)",
                    maxLength: 300,
                    nullable: true);
                migrationBuilder.AddColumn<bool>(
                    name: "BlobReadEmailAppendDate",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: false);
                migrationBuilder.AddColumn<string>(
                    name: "BlobInputFilePattern",
                    table: "AppConfigurations",
                    type: "nvarchar(300)",
                    maxLength: 300,
                    nullable: true);
                migrationBuilder.AddColumn<bool>(
                    name: "BlobInputAppendDate",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: true);
                migrationBuilder.AddColumn<string>(
                    name: "BlobArchiveFilePattern",
                    table: "AppConfigurations",
                    type: "nvarchar(300)",
                    maxLength: 300,
                    nullable: true);
                migrationBuilder.AddColumn<bool>(
                    name: "BlobArchiveAppendDate",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: true);
                migrationBuilder.AddColumn<bool>(
                    name: "NotifyOnSuccess",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: false);
                migrationBuilder.AddColumn<bool>(
                    name: "NotifyOnFileNotFound",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: true);
                migrationBuilder.AddColumn<bool>(
                    name: "NotifyOnDataIssue",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: true);
                migrationBuilder.AddColumn<bool>(
                    name: "SqlPushEnabled",
                    table: "AppConfigurations",
                    type: "bit",
                    nullable: false,
                    defaultValue: false);
                migrationBuilder.AddColumn<string>(
                    name: "SqlTargetTable",
                    table: "AppConfigurations",
                    type: "nvarchar(300)",
                    maxLength: 300,
                    nullable: true);
                migrationBuilder.AddColumn<string>(
                    name: "SqlColumnMappingJson",
                    table: "AppConfigurations",
                    type: "nvarchar(max)",
                    nullable: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Agent Designer columns are non-destructively removed for rollback on non-SQLite.
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropColumn(name: "BlobEnabled",             table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "BlobReadEmailId",         table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "BlobReadEmailAppendDate", table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "BlobInputFilePattern",    table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "BlobInputAppendDate",     table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "BlobArchiveFilePattern",  table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "BlobArchiveAppendDate",   table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "NotifyOnSuccess",         table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "NotifyOnFileNotFound",    table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "NotifyOnDataIssue",       table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "SqlPushEnabled",          table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "SqlTargetTable",          table: "AppConfigurations");
                migrationBuilder.DropColumn(name: "SqlColumnMappingJson",    table: "AppConfigurations");
            }
        }
    }
}
