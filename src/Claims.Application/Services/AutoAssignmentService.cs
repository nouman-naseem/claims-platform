using Claims.Application.Abstractions;
using Claims.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Claims.Application.Services;

/// <summary>
/// Triggered asynchronously by the ClaimSubmitted Kafka event. Assigns the claim to the least-loaded
/// officer, preferring the claim's market. Idempotent: redelivered events are no-ops.
/// </summary>
public sealed class AutoAssignmentService
{
    /// <summary>Actor recorded on audit entries for system-initiated actions.</summary>
    public static readonly Guid SystemActor = Guid.Empty;

    private readonly IClaimsDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutoAssignmentService> _log;

    public AutoAssignmentService(IClaimsDbContext db, TimeProvider clock, ILogger<AutoAssignmentService> log)
    {
        _db = db;
        _clock = clock;
        _log = log;
    }

    public async Task<bool> TryAutoAssignAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await _db.Claims.Include(c => c.Assignments).FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null)
        {
            _log.LogWarning("Auto-assign: claim {ClaimId} not found", claimId);
            return false;
        }
        if (claim.AssignedOfficerId is not null || claim.Status != ClaimStatus.Submitted)
        {
            _log.LogInformation("Auto-assign: claim {ClaimNumber} already handled, skipping", claim.ClaimNumber);
            return false;
        }

        var officers = await _db.Users.Where(u => u.Role == UserRole.ClaimsOfficer && u.IsActive).ToListAsync(ct);
        if (officers.Count == 0)
        {
            _log.LogWarning("Auto-assign: no active officers available");
            return false;
        }

        var load = await _db.Claims
            .Where(c => c.AssignedOfficerId != null && c.Status != ClaimStatus.Approved && c.Status != ClaimStatus.Rejected)
            .GroupBy(c => c.AssignedOfficerId)
            .Select(g => new { OfficerId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var loadByOfficer = load.ToDictionary(x => x.OfficerId!.Value, x => x.Count);

        var candidates = officers.Where(o => o.Market == claim.Market).ToList();
        if (candidates.Count == 0) candidates = officers;
        var chosen = candidates.OrderBy(o => loadByOfficer.GetValueOrDefault(o.Id)).ThenBy(o => o.FullName).First();

        claim.AssignTo(chosen.Id, SystemActor, _clock.GetUtcNow().UtcDateTime);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Auto-assigned claim {ClaimNumber} to officer {OfficerId}", claim.ClaimNumber, chosen.Id);
        return true;
    }
}
