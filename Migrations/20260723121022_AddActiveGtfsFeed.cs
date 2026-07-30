using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveGtfsFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "GtfsImportRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve the currently served feed when this migration is applied
            // to an existing database.
            migrationBuilder.Sql("""
                UPDATE "GtfsImportRuns"
                SET "IsActive" = true
                WHERE "Id" = (
                    SELECT "Id"
                    FROM "GtfsImportRuns"
                    WHERE "Status" = 'Completed'
                    ORDER BY "FinishedAt" DESC NULLS LAST, "Id" DESC
                    LIMIT 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsImportRuns_IsActive",
                table: "GtfsImportRuns",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GtfsImportRuns_ActiveFeedMustBeCompleted",
                table: "GtfsImportRuns",
                sql: "NOT \"IsActive\" OR \"Status\" = 'Completed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GtfsImportRuns_IsActive",
                table: "GtfsImportRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GtfsImportRuns_ActiveFeedMustBeCompleted",
                table: "GtfsImportRuns");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "GtfsImportRuns");
        }
    }
}


