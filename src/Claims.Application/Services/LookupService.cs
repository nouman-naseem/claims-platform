using Claims.Application.Abstractions;
using Claims.Application.Contracts;
using Claims.Application.Exceptions;
using Claims.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Claims.Application.Services;

public sealed class LookupService
{
    private readonly IClaimsDbContext _db;
    private readonly ICurrentUser _user;

    public LookupService(IClaimsDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<IReadOnlyList<PolicyDto>> GetMyPoliciesAsync(CancellationToken ct)
    {
        var claimantId = _user.ClaimantId ?? throw new ForbiddenException();
        var policies = await _db.Policies.AsNoTracking().Where(p => p.ClaimantId == claimantId)
            .OrderBy(p => p.PolicyNumber).ToListAsync(ct);
        return policies.Select(p => new PolicyDto(p.Id, p.PolicyNumber, p.Type, p.Market, p.Currency,
            p.CoverageLimit, p.StartDate, p.EndDate, p.Description)).ToList();
    }

    public async Task<IReadOnlyList<OfficerDto>> GetOfficersAsync(CancellationToken ct)
    {
        var officers = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.ClaimsOfficer && u.IsActive)
            .OrderBy(u => u.FullName).ToListAsync(ct);

        var open = await _db.Claims.AsNoTracking()
            .Where(c => c.AssignedOfficerId != null && c.Status != ClaimStatus.Approved && c.Status != ClaimStatus.Rejected)
            .GroupBy(c => c.AssignedOfficerId)
            .Select(g => new { OfficerId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var openByOfficer = open.ToDictionary(x => x.OfficerId!.Value, x => x.Count);

        return officers.Select(o => new OfficerDto(o.Id, o.FullName, o.Market, openByOfficer.GetValueOrDefault(o.Id))).ToList();
    }
}
