using Claims.Domain.Entities;
using Claims.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Claims.Application.Abstractions;

/// <summary>Persistence port. Implemented by the EF Core DbContext in Infrastructure.</summary>
public interface IClaimsDbContext
{
    DbSet<User> Users { get; }
    DbSet<Claimant> Claimants { get; }
    DbSet<Policy> Policies { get; }
    DbSet<Claim> Claims { get; }
    DbSet<Decision> Decisions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    Guid UserId { get; }
    UserRole Role { get; }
    Market Market { get; }
    Guid? ClaimantId { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed record IssuedToken(string Token, DateTime ExpiresAt);

public interface IJwtTokenGenerator
{
    IssuedToken Generate(User user, Guid? claimantId);
}

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
