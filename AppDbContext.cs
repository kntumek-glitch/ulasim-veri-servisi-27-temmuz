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
}