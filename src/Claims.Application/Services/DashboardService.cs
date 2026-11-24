using Claims.Application.Abstractions;
using Claims.Application.Contracts;
using Claims.Application.Exceptions;
using Claims.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Claims.Application.Services;

public sealed class DashboardService
{
    private readonly IClaimsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public DashboardService(IClaimsDbContext db, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        if (_user.Role != UserRole.Manager) throw new ForbiddenException();

        var now = _clock.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(-ClaimsPolicy.SlaDays);
        var claims = _db.Claims.AsNoTracking();

        var total = await claims.CountAsync(ct);
        var outstanding = await claims.CountAsync(c => c.Status != ClaimStatus.Approved && c.Status != ClaimStatus.Rejected, ct);
        var pending = await claims.CountAsync(c => c.Status == ClaimStatus.UnderReview, ct);
        var unassigned = await claims.CountAsync(c => c.AssignedOfficerId == null && c.Status == ClaimStatus.Submitted, ct);
        var overdue = await claims.CountAsync(c =>
            (c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview) && c.SubmittedAt < cutoff, ct);

        var byStatus = (await claims.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Select(x => new CountByDto(x.Key.ToString(), x.Count)).OrderBy(x => x.Key).ToList();
        var byType = (await claims.GroupBy(c => c.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Select(x => new CountByDto(x.Key.ToString(), x.Count)).OrderBy(x => x.Key).ToList();
        var byMarket = (await claims.GroupBy(c => c.Market).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Select(x => new CountByDto(x.Key.ToString(), x.Count)).OrderBy(x => x.Key).ToList();

        // Decimal SUM is aggregated in memory over a narrow projection: it keeps the query portable
        // across SQL Server and the SQLite fallback. Bounded by outstanding claims; see TRADE-OFFS.md.
        var exposureRows = await claims
            .Where(c => c.Status != ClaimStatus.Approved && c.Status != ClaimStatus.Rejected)
            .Select(c => new { c.Market, c.Currency, c.EstimatedExposure })
            .ToListAsync(ct);
        var exposureByMarket = exposureRows.GroupBy(r => new { r.Market, r.Currency })
            .Select(g =>
            {
                var local = g.Sum(r => r.EstimatedExposure);
                return new ExposureByMarketDto(g.Key.Market.ToString(), g.Key.Currency, local, ClaimsPolicy.ToUsd(g.Key.Currency, local));
            })
            .OrderBy(x => x.Market).ToList();
        var totalExposureUsd = exposureByMarket.Sum(x => x.ExposureUsd);

        var workload = await BuildWorkloadAsync(cutoff, ct);

        return new DashboardDto(total, outstanding, pending, unassigned, overdue, totalExposureUsd, ClaimsPolicy.SlaDays,
            byStatus, byType, byMarket, exposureByMarket, workload);
    }

    private async Task<IReadOnlyList<OfficerWorkloadDto>> BuildWorkloadAsync(DateTime cutoff, CancellationToken ct)
    {
        var officers = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.ClaimsOfficer && u.IsActive).OrderBy(u => u.FullName).ToListAsync(ct);

        var open = (await _db.Claims.AsNoTracking()
                .Where(c => c.AssignedOfficerId != null && c.Status != ClaimStatus.Approved && c.Status != ClaimStatus.Rejected)
                .Select(c => new
                {
                    OfficerId = c.AssignedOfficerId!.Value,
                    Overdue = (c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview) && c.SubmittedAt < cutoff
                })
                .ToListAsync(ct))
            .GroupBy(x => x.OfficerId)
            .ToDictionary(g => g.Key, g => (Open: g.Count(), Overdue: g.Count(x => x.Overdue)));

        var decisions = (await (from d in _db.Decisions.AsNoTracking()
                                join c in _db.Claims.AsNoTracking() on d.ClaimId equals c.Id
                                select new { d.DecidedByUserId, d.Outcome, d.DecidedAt, c.SubmittedAt }).ToListAsync(ct))
            .GroupBy(x => x.DecidedByUserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return officers.Select(o =>
        {
            var (openCount, overdueCount) = open.GetValueOrDefault(o.Id);
            if (!decisions.TryGetValue(o.Id, out var decided))
                return new OfficerWorkloadDto(o.Id, o.FullName, o.Market.ToString(), openCount, overdueCount, 0, 0, 0);
            var avgDays = Math.Round(decided.Average(d => (d.DecidedAt - d.SubmittedAt).TotalDays), 1);
            return new OfficerWorkloadDto(o.Id, o.FullName, o.Market.ToString(), openCount, overdueCount,
                decided.Count, decided.Count(d => d.Outcome == DecisionOutcome.Approved), avgDays);
        }).ToList();
    }
}
