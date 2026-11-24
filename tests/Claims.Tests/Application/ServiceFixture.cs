using Claims.Application.Abstractions;
using Claims.Application.Contracts;
using Claims.Application.Services;
using Claims.Domain.Entities;
using Claims.Domain.Enums;
using Claims.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Claims.Tests.Application;

internal sealed class FakeUser : ICurrentUser
{
    public Guid UserId { get; init; }
    public UserRole Role { get; init; }
    public Market Market { get; init; } = Market.Singapore;
    public Guid? ClaimantId { get; init; }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTime utcNow) => _now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    public override DateTimeOffset GetUtcNow() => _now;
}

internal sealed class InMemoryStorage : IFileStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task SaveAsync(string key, Stream content, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        Files[key] = ms.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Real EF Core on in-memory SQLite (not mocks): exercises the actual mappings, the outbox and the queries.
/// Every service call gets a fresh DbContext, like a real request scope.
/// </summary>
internal sealed class ServiceFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ClaimsDbContext> _options;

    public InMemoryStorage Storage { get; } = new();
    public FakeUser Manager { get; }
    public FakeUser OfficerA { get; }
    public FakeUser OfficerB { get; }
    public FakeUser Claimant1 { get; }
    public FakeUser Claimant2 { get; }
    public Guid Policy1 { get; }
    public Guid Policy2 { get; }

    public ServiceFixture()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlite(_connection, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Options;

        using var db = NewDb();
        db.Database.EnsureCreated();

        var now = TestData.Now;
        var manager = new User("manager@test", "x", "Mgr", UserRole.Manager, Market.Singapore, now);
        var officerA = new User("a@test", "x", "Officer A", UserRole.ClaimsOfficer, Market.Singapore, now);
        var officerB = new User("b@test", "x", "Officer B", UserRole.ClaimsOfficer, Market.Singapore, now);
        var u1 = new User("c1@test", "x", "Claimant One", UserRole.Claimant, Market.Singapore, now);
        var u2 = new User("c2@test", "x", "Claimant Two", UserRole.Claimant, Market.Singapore, now);
        var c1 = new Claimant(u1.Id, u1.FullName, u1.Email, "+65 1234 5678", Market.Singapore);
        var c2 = new Claimant(u2.Id, u2.FullName, u2.Email, "+65 8765 4321", Market.Singapore);
        var p1 = TestData.NewPolicy(c1.Id);
        var p2 = TestData.NewPolicy(c2.Id);
        db.Users.AddRange(manager, officerA, officerB, u1, u2);
        db.Claimants.AddRange(c1, c2);
        db.Policies.AddRange(p1, p2);
        db.SaveChanges();

        Manager = new FakeUser { UserId = manager.Id, Role = UserRole.Manager };
        OfficerA = new FakeUser { UserId = officerA.Id, Role = UserRole.ClaimsOfficer };
        OfficerB = new FakeUser { UserId = officerB.Id, Role = UserRole.ClaimsOfficer };
        Claimant1 = new FakeUser { UserId = u1.Id, Role = UserRole.Claimant, ClaimantId = c1.Id };
        Claimant2 = new FakeUser { UserId = u2.Id, Role = UserRole.Claimant, ClaimantId = c2.Id };
        Policy1 = p1.Id;
        Policy2 = p2.Id;
    }

    public ClaimsDbContext NewDb() => new(_options);

    public ClaimService Claims(FakeUser user, DateTime? now = null) =>
        new(NewDb(), user, new FixedTimeProvider(now ?? TestData.Now), Storage, NullLogger<ClaimService>.Instance);

    public DashboardService Dashboard(FakeUser user, DateTime? now = null) =>
        new(NewDb(), user, new FixedTimeProvider(now ?? TestData.Now));

    public AutoAssignmentService AutoAssign() =>
        new(NewDb(), new FixedTimeProvider(TestData.Now), NullLogger<AutoAssignmentService>.Instance);

    public Task<ClaimDetailDto> SubmitAsync(FakeUser claimant, Guid? policyId = null, decimal amount = 5_000m) =>
        Claims(claimant).SubmitAsync(new SubmitClaimRequest(
            policyId ?? (claimant == Claimant2 ? Policy2 : Policy1),
            TestData.Now.AddDays(-2), "Orchard Road", "Rear-end collision at the traffic lights.", amount), default);

    public void Dispose() => _connection.Dispose();
}
