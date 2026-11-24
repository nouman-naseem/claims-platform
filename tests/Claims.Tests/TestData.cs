using Claims.Domain.Entities;
using Claims.Domain.Enums;

namespace Claims.Tests;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

    public static Policy NewPolicy(Guid claimantId, decimal limit = 60_000m, ClaimType type = ClaimType.Motor,
        Market market = Market.Singapore) =>
        new($"SG-MOT-{Guid.NewGuid().ToString("N")[..6]}", "holder@example.com", type, market, limit,
            Now.AddDays(-100), Now.AddDays(100), "Toyota Corolla", claimantId);

    public static Claim NewClaim(Guid? claimantId = null, Policy? policy = null, decimal amount = 5_000m)
    {
        var cid = claimantId ?? Guid.NewGuid();
        policy ??= NewPolicy(cid);
        return Claim.Submit(policy, cid, Now.AddDays(-2), "Orchard Road", "Rear-end collision at lights.",
            amount, Guid.NewGuid(), $"CLM-SG-TEST-{Guid.NewGuid().ToString("N")[..6]}", Now);
    }
}
