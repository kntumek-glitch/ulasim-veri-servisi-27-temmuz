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
    public async Task CleanInstall_RunAllMigrations_ShouldCompleteSuccessfully()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        await using var context = new AppDbContext(options);
        
        Func<Task> act = async () => await context.Database.MigrateAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ForwardOnlyMigration_WhenDatabaseHasLegacyData_RunsSuccessfullyAndBackfillsData()
    {
        // Arrange: Setup DbContext targeting the Testcontainer
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        await using var context = new AppDbContext(options);
        var migrator = context.GetService<IMigrator>();

        // 1. Migrate up to VersionedFeed (the original one, without backfill)
        // Since the DB is empty, this will succeed even with AlterColumn and AddForeignKey
        await migrator.MigrateAsync("20260728123556_VersionedFeed");

        // Temporarily drop FKs to simulate "legacy data with GtfsImportRunId = 0"
        // This simulates a database where developers manually dropped constraints or used bulk insert without constraints.
        await context.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE ""GtfsTrips"" DROP CONSTRAINT ""FK_GtfsTrips_GtfsImportRuns_GtfsImportRunId"";
            ALTER TABLE ""GtfsRoutes"" DROP CONSTRAINT ""FK_GtfsRoutes_GtfsImportRuns_GtfsImportRunId"";
            ALTER TABLE ""GtfsStops"" DROP CONSTRAINT ""FK_GtfsStops_GtfsImportRuns_GtfsImportRunId"";
        ");

        // 2. Insert raw SQL data representing the legacy state (with GtfsImportRunId = 0)
        await context.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""GtfsRoutes"" (""Id"", ""RouteId"", ""RouteShortName"", ""RouteLongName"", ""RouteType"", ""GtfsImportRunId"")
            VALUES (1, 'R1', 'Short', 'Long', 3, 0);

            INSERT INTO ""GtfsTrips"" (""Id"", ""RouteId"", ""GtfsRouteId"", ""ServiceId"", ""TripId"", ""DirectionId"", ""GtfsImportRunId"")
            VALUES (1, 'R1', 1, 'SVC1', 'T1', 0, 0);

            INSERT INTO ""GtfsStops"" (""Id"", ""StopId"", ""StopCode"", ""StopName"", ""StopLat"", ""StopLon"", ""GtfsImportRunId"")
            VALUES (1, 'S1', 'SC1', 'Stop 1', 38.0, 27.0, 0);
        ");

        // Act: Apply all remaining migrations (which includes our new BackfillVersionedFeed)
        Func<Task> act = async () => await context.Database.MigrateAsync();

        // Assert: The migration should succeed
        await act.Should().NotThrowAsync();

        // Verify that the backfill worked properly and a Dummy GtfsImportRun was generated
        var runIdResult = await context.Database.SqlQueryRaw<int>(@"SELECT ""Id"" AS ""Value"" FROM ""GtfsImportRuns"" WHERE ""SourceUrl"" = 'http://dummy.url' AND ""IsActive"" = false LIMIT 1").ToListAsync();
        runIdResult.Should().NotBeEmpty();
        int activeRunId = runIdResult.First();

        // Verify that existing records now have the activeRunId
        var tripRunId = await context.Database.SqlQueryRaw<int>($@"SELECT ""GtfsImportRunId"" AS ""Value"" FROM ""GtfsTrips"" WHERE ""TripId"" = 'T1'").FirstOrDefaultAsync();
        tripRunId.Should().Be(activeRunId);

        var routeRunId = await context.Database.SqlQueryRaw<int>($@"SELECT ""GtfsImportRunId"" AS ""Value"" FROM ""GtfsRoutes"" WHERE ""RouteId"" = 'R1'").FirstOrDefaultAsync();
        routeRunId.Should().Be(activeRunId);
    }
}

