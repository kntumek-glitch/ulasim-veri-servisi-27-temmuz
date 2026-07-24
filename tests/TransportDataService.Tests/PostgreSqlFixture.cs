using Testcontainers.PostgreSql;

namespace TransportDataService.Tests;

public class PostgreSqlFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; }

    public PostgreSqlFixture()
    {
        Container = new PostgreSqlBuilder("postgres:15-alpine")
            .WithDatabase("transport_test_db")
            .WithUsername("postgres")
            .WithPassword("postgres123")
            .Build();
    }

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync().AsTask();
    }
}