using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class BackfillVersionedFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    runId int;
                BEGIN
                    SELECT ""Id"" INTO runId FROM ""GtfsImportRuns"" WHERE ""Status"" = 'Completed' ORDER BY ""Id"" DESC LIMIT 1;
                    IF runId IS NULL THEN
                        INSERT INTO ""GtfsImportRuns"" (""SourceUrl"", ""DownloadedAt"", ""StartedAt"", ""Status"", ""FileHash"", ""AgencyCount"", ""RouteCount"", ""StopCount"", ""TripCount"", ""StopTimeCount"", ""ShapePointCount"", ""FailedRecordCount"", ""IsActive"") 
                        VALUES ('http://dummy.url', NOW(), NOW(), 'Completed', 'dummy-hash', 0, 0, 0, 0, 0, 0, 0, false) RETURNING ""Id"" INTO runId;
                    END IF;

                    UPDATE ""GtfsTrips"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                    UPDATE ""GtfsStopTimes"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                    UPDATE ""GtfsStops"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                    UPDATE ""GtfsShapePoints"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                    UPDATE ""GtfsRoutes"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                    UPDATE ""GtfsCalendars"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                    UPDATE ""GtfsCalendarDates"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                    UPDATE ""GtfsAgencies"" SET ""GtfsImportRunId"" = runId WHERE ""GtfsImportRunId"" = 0 OR ""GtfsImportRunId"" IS NULL;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
