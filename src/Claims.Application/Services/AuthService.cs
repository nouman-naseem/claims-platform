using Claims.Application.Abstractions;
using Claims.Application.Contracts;
using Claims.Application.Exceptions;
using Claims.Domain.Entities;
using Claims.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Claims.Application.Services;

public sealed class AuthService
{
    private readonly IClaimsDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _tokens;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthService> _log;

    public AuthService(IClaimsDbContext db, IPasswordHasher hasher, IJwtTokenGenerator tokens, TimeProvider clock, ILogger<AuthService> log)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
        _log = log;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Same error for unknown user, wrong password and disabled account: no account enumeration.
        if (user is null || !user.IsActive || !_hasher.Verify(request.Password, user.PasswordHash))
        {
            _log.LogWarning("Failed login attempt for {Email}", email);
            throw new AuthenticationFailedException();
        }

        Guid? claimantId = null;
        if (user.Role == UserRole.Claimant)
            claimantId = await _db.Claimants.Where(c => c.UserId == user.Id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

        var token = _tokens.Generate(user, claimantId);
        _log.LogInformation("User {UserId} ({Role}) logged in", user.Id, user.Role);
        return new AuthResponse(token.Token, token.ExpiresAt, ToDto(user));
    }

    public async Task<AuthResponse> RegisterClaimantAsync(RegisterClaimantRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var user = new User(email, _hasher.Hash(request.Password), request.FullName, UserRole.Claimant, request.Market, now);
        var claimant = new Claimant(user.Id, request.FullName, email, request.Phone, request.Market);
        _db.Users.Add(user);
        _db.Claimants.Add(claimant);

        // Link pre-issued policies that carry this holder email (policies originate upstream).
        var policies = await _db.Policies.Where(p => p.ClaimantId == null && p.HolderEmail == email).ToListAsync(ct);
        foreach (var policy in policies) policy.LinkTo(claimant.Id);

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Claimant {UserId} registered, {PolicyCount} policies linked", user.Id, policies.Count);

        var token = _tokens.Generate(user, claimant.Id);
        return new AuthResponse(token.Token, token.ExpiresAt, ToDto(user));
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.FullName, u.Role, u.Market);
}
