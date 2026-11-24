using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Claims.Tests.Integration;

/// <summary>
/// Hosts the real API (real pipeline, auth, validation, EF mappings, seed data) on a temp SQLite file with
/// Kafka disabled. Settings are passed as environment variables because Program reads configuration eagerly.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"claims-it-{Guid.NewGuid():N}.db");
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), $"claims-it-uploads-{Guid.NewGuid():N}");

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Kafka__Enabled", "false");
        Environment.SetEnvironmentVariable("Seed__Enabled", "true");
        Environment.SetEnvironmentVariable("Storage__Path", _uploads);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_uploads)) Directory.Delete(_uploads, true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }
}
