using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using TransportDataService;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

public class MigrationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("ulasim_migration_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    [Fact]
    public async Task VersionedFeedMigration_WhenDatabaseIsPopulated_RunsSuccessfullyAndBackfillsData()
    {
        // Arrange: Setup DbContext targeting the Testcontainer
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        await using var context = new AppDbContext(options);
        var migrator = context.GetService<IMigrator>();

        // 1. Migrate up to the point just before "VersionedFeed"
        // The migration right before VersionedFeed is "20260727072403_AddGtfsRouteFields"
        await migrator.MigrateAsync("20260727072403_AddGtfsRouteFields");

        // 2. Insert raw SQL data representing the old schema (without GtfsImportRunId columns)
        // Note: Using raw SQL because EF models represent the current (future) state
        await context.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""GtfsAgencies"" (""Id"", ""AgencyId"", ""AgencyName"", ""AgencyUrl"", ""AgencyTimezone"", ""AgencyLang"", ""AgencyPhone"")
            VALUES (1, 'AG1', 'Test Agency', 'http://test.com', 'Europe/Istanbul', 'tr', '123');

            INSERT INTO ""GtfsRoutes"" (""Id"", ""RouteId"", ""RouteShortName"", ""RouteLongName"", ""RouteType"")
            VALUES (1, 'R1', 'Short', 'Long', 3);

            INSERT INTO ""GtfsTrips"" (""Id"", ""RouteId"", ""GtfsRouteId"", ""ServiceId"", ""TripId"", ""DirectionId"")
            VALUES (1, 'R1', 1, 'SVC1', 'T1', 0);

            INSERT INTO ""GtfsStops"" (""Id"", ""StopId"", ""StopCode"", ""StopName"", ""StopLat"", ""StopLon"")
            VALUES (1, 'S1', 'SC1', 'Stop 1', 38.0, 27.0);
        ");

        // Act: Apply the VersionedFeed migration on the populated database
        // This should trigger the new backfill SQL we wrote in the migration file
        Func<Task> act = async () => await migrator.MigrateAsync("20260728123556_VersionedFeed");

        // Assert: The migration should succeed without throwing a Foreign Key or Non-Null constraint error
        await act.Should().NotThrowAsync();

        // 3. Verify that the backfill worked properly and a GtfsImportRun was generated or used
        // Since there were no existing runs, our migration SQL should have created one.
        var runIdResult = await context.Database.SqlQueryRaw<int>(@"SELECT ""Id"" AS ""Value"" FROM ""GtfsImportRuns"" WHERE ""Status"" = 'Completed' ORDER BY ""Id"" DESC LIMIT 1").ToListAsync();
        runIdResult.Should().NotBeEmpty();
        int activeRunId = runIdResult.First();

        // Verify that existing records now have the activeRunId
        var tripRunId = await context.Database.SqlQueryRaw<int>($@"SELECT ""GtfsImportRunId"" AS ""Value"" FROM ""GtfsTrips"" WHERE ""TripId"" = 'T1'").FirstOrDefaultAsync();
        tripRunId.Should().Be(activeRunId);

        var routeRunId = await context.Database.SqlQueryRaw<int>($@"SELECT ""GtfsImportRunId"" AS ""Value"" FROM ""GtfsRoutes"" WHERE ""RouteId"" = 'R1'").FirstOrDefaultAsync();
        routeRunId.Should().Be(activeRunId);
    }
}
