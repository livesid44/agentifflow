using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentifFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlobJobNotificationRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite does not support ALTER TABLE ADD COLUMN with a CHECK constraint or
                // without IF NOT EXISTS.  Use the same table-recreation pattern used elsewhere
                // in this project so the migration is idempotent on databases that were
                // pre-created with EnsureCreated or restored from an older backup.
                migrationBuilder.Sql(@"
                    CREATE TABLE IF NOT EXISTS ""BlobWatcherJobs_v2"" (
                        ""Id""              INTEGER NOT NULL CONSTRAINT ""PK_BlobWatcherJobs"" PRIMARY KEY AUTOINCREMENT,
                        ""BlobName""        TEXT    NOT NULL DEFAULT '',
                        ""ContainerName""   TEXT    NULL,
                        ""Status""          TEXT    NOT NULL DEFAULT 'Detected',
                        ""RetryCount""      INTEGER NOT NULL DEFAULT 0,
                        ""ErrorMessage""    TEXT    NULL,
                        ""LogDetails""      TEXT    NULL,
                        ""RowsInserted""    INTEGER NULL,
                        ""DetectedAt""      TEXT    NOT NULL DEFAULT '',
                        ""CompletedAt""     TEXT    NULL,
                        ""UpdatedAt""       TEXT    NOT NULL DEFAULT '',
                        ""NotificationRef"" TEXT    NULL,
                        ""UserReply""       TEXT    NULL
                    );
                ");

                migrationBuilder.Sql(@"
                    INSERT OR IGNORE INTO ""BlobWatcherJobs_v2""
                        (""Id"", ""BlobName"", ""ContainerName"", ""Status"", ""RetryCount"",
                         ""ErrorMessage"", ""LogDetails"", ""RowsInserted"",
                         ""DetectedAt"", ""CompletedAt"", ""UpdatedAt"",
                         ""NotificationRef"", ""UserReply"")
                    SELECT ""Id"", ""BlobName"", ""ContainerName"", ""Status"", ""RetryCount"",
                           ""ErrorMessage"", ""LogDetails"", ""RowsInserted"",
                           ""DetectedAt"", ""CompletedAt"", ""UpdatedAt"",
                           CASE WHEN (SELECT COUNT(*) FROM pragma_table_info('BlobWatcherJobs') WHERE name='NotificationRef') > 0
                                THEN ""NotificationRef"" ELSE NULL END,
                           CASE WHEN (SELECT COUNT(*) FROM pragma_table_info('BlobWatcherJobs') WHERE name='UserReply') > 0
                                THEN ""UserReply"" ELSE NULL END
                    FROM ""BlobWatcherJobs"";
                ");

                migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""BlobWatcherJobs"";");
                migrationBuilder.Sql(@"ALTER TABLE ""BlobWatcherJobs_v2"" RENAME TO ""BlobWatcherJobs"";");
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "NotificationRef",
                    table: "BlobWatcherJobs",
                    type: "nvarchar(20)",
                    maxLength: 20,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "UserReply",
                    table: "BlobWatcherJobs",
                    type: "nvarchar(2000)",
                    maxLength: 2000,
                    nullable: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite: leave columns in place on rollback (table recreation is not reversible here).
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropColumn(name: "NotificationRef", table: "BlobWatcherJobs");
                migrationBuilder.DropColumn(name: "UserReply",        table: "BlobWatcherJobs");
            }
        }
    }
}
