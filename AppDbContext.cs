using Microsoft.EntityFrameworkCore;
using TransportDataService.Domain;

namespace TransportDataService;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Stop> Stops { get; set; }

    public DbSet<StopRoute> StopRoutes { get; set; }

    public DbSet<ImportRun> ImportRuns { get; set; }
    

    public DbSet<ExternalApiLog> ExternalApiLogs { get; set; }

    public DbSet<GtfsAgency> GtfsAgencies { get; set; }

    public DbSet<GtfsRoute> GtfsRoutes { get; set; }

    public DbSet<GtfsStop> GtfsStops { get; set; }

    public DbSet<GtfsTrip> GtfsTrips { get; set; }

    public DbSet<GtfsStopTime> GtfsStopTimes { get; set; }

    public DbSet<GtfsCalendar> GtfsCalendars { get; set; }

    public DbSet<GtfsCalendarDate> GtfsCalendarDates { get; set; }

    public DbSet<GtfsShapePoint> GtfsShapePoints { get; set; }

    public DbSet<GtfsImportRun> GtfsImportRuns { get; set; }
    
    public DbSet<GtfsImportPhase> GtfsImportPhases { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Stop>()
            .HasIndex(x => x.ExternalStopId)
            .IsUnique();

        modelBuilder.Entity<StopRoute>()
            .HasIndex(x => new
            {
                x.StopId,
                x.RouteNumber
            })
            .IsUnique();

        modelBuilder.Entity<GtfsImportRun>()
            .HasIndex(x => x.FileHash)
            .IsUnique(false);

        modelBuilder.Entity<GtfsImportPhase>()
            .HasOne(x => x.GtfsImportRun)
            .WithMany(x => x.Phases)
            .HasForeignKey(x => x.GtfsImportRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GtfsTrip>()
            .HasMany(x => x.StopTimes)
            .WithOne(x => x.Trip)
            .HasForeignKey(x => x.GtfsTripId);


        modelBuilder.Entity<GtfsTrip>()
    .HasOne(x => x.Route)
    .WithMany(x => x.Trips)
    .HasForeignKey(x => x.GtfsRouteId)
    .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GtfsStop>()
            .HasMany(x => x.StopTimes)
            .WithOne(x => x.Stop)
            .HasForeignKey(x => x.GtfsStopId);

        modelBuilder.Entity<GtfsAgency>()
            .HasIndex(x => new { x.AgencyId, x.GtfsImportRunId })
            .IsUnique();

        modelBuilder.Entity<GtfsRoute>()
            .HasIndex(x => new { x.RouteId, x.GtfsImportRunId })
            .IsUnique();

        modelBuilder.Entity<GtfsStop>()
            .HasIndex(x => new { x.StopId, x.GtfsImportRunId })
            .IsUnique();

        modelBuilder.Entity<GtfsTrip>()
            .HasIndex(x => new { x.TripId, x.GtfsImportRunId })
            .IsUnique();

        modelBuilder.Entity<GtfsStopTime>()
            .HasIndex(x => new { x.StopId, x.DepartureSeconds });

        modelBuilder.Entity<GtfsStopTime>()
            .HasIndex(x => new { x.GtfsTripId, x.StopSequence });

        modelBuilder.Entity<GtfsAgency>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
        modelBuilder.Entity<GtfsRoute>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
        modelBuilder.Entity<GtfsStop>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
        modelBuilder.Entity<GtfsTrip>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
        modelBuilder.Entity<GtfsStopTime>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
        modelBuilder.Entity<GtfsCalendar>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
        modelBuilder.Entity<GtfsCalendarDate>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
        modelBuilder.Entity<GtfsShapePoint>().HasQueryFilter(x => x.GtfsImportRun.IsActive);
    }
}