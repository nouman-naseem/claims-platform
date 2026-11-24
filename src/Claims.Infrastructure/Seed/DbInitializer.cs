using Claims.Application.Abstractions;
using Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Claims.Infrastructure.Seed;

public static class DbInitializer
{
    /// <summary>
    /// Creates the schema (with retries while the database container starts) and optionally seeds demo data.
    /// EnsureCreated is used instead of migrations to keep the take-home runnable with zero tooling;
    /// see docs/TRADE-OFFS.md for the migration path.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, bool seed, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<ClaimsDbContext>>();

        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync(ct);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                log.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        if (seed)
        {
            await DbSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>(), log, ct);
        }
    }
}
