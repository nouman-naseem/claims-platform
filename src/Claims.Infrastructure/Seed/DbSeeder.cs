using Claims.Application.Abstractions;
using Claims.Domain.Entities;
using Claims.Domain.Enums;
using Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Claims.Infrastructure.Seed;

/// <summary>
/// Demo data across six APAC markets. Claims are built through the aggregate's own methods so every seeded
/// claim has a valid history. Seeded outbox rows are marked processed so demo data does not flood Kafka.
/// </summary>
public static class DbSeeder
{
    public const string DemoPassword = "Password123!";

    private static readonly Dictionary<Market, decimal> Scale = new()
    {
        [Market.Singapore] = 1m, [Market.HongKong] = 6m, [Market.Australia] = 1.2m,
        [Market.Japan] = 100m, [Market.Malaysia] = 3.5m, [Market.Thailand] = 26m
    };

    private sealed record ClaimantSpec(string Email, string Name, Market Market, string Phone, string Vehicle, string Address);

    public static async Task SeedAsync(ClaimsDbContext db, IPasswordHasher hasher, TimeProvider clock, ILogger log, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var hash = hasher.Hash(DemoPassword); // one hash for all demo accounts keeps seeding fast
        var rng = new Random(42);

        var manager = new User("manager@claims.example", hash, "Maria Tan", UserRole.Manager, Market.Singapore, now);
        var officers = new[]
        {
            new User("officer.sg@claims.example", hash, "Priya Menon", UserRole.ClaimsOfficer, Market.Singapore, now),
            new User("officer.sg2@claims.example", hash, "Daniel Koh", UserRole.ClaimsOfficer, Market.Singapore, now),
            new User("officer.hk@claims.example", hash, "Grace Wong", UserRole.ClaimsOfficer, Market.HongKong, now),
            new User("officer.au@claims.example", hash, "Tom Fraser", UserRole.ClaimsOfficer, Market.Australia, now),
            new User("officer.au2@claims.example", hash, "Chloe Nguyen", UserRole.ClaimsOfficer, Market.Australia, now),
            new User("officer.jp@claims.example", hash, "Yuki Tanaka", UserRole.ClaimsOfficer, Market.Japan, now),
            new User("officer.my@claims.example", hash, "Farah Ismail", UserRole.ClaimsOfficer, Market.Malaysia, now),
            new User("officer.th@claims.example", hash, "Narin Chaiyasit", UserRole.ClaimsOfficer, Market.Thailand, now)
        };
        db.Users.Add(manager);
        db.Users.AddRange(officers);

        var specs = new[]
        {
            new ClaimantSpec("claimant.sg1@example.com", "Wei Ling Chua", Market.Singapore, "+65 8123 4567", "Toyota Corolla Altis SMB1234X", "12 Orchard View #05-02, Singapore 238888"),
            new ClaimantSpec("claimant.sg2@example.com", "Rajesh Nair", Market.Singapore, "+65 9234 5678", "Honda Civic SKA5678Z", "88 Tampines Ave 4 #12-10, Singapore 529510"),
            new ClaimantSpec("claimant.hk1@example.com", "Chun Kit Lam", Market.HongKong, "+852 6123 4567", "Toyota Alphard AB1234", "Flat 8B, 25 Tai Koo Shing Rd, Hong Kong"),
            new ClaimantSpec("claimant.au1@example.com", "Olivia Bennett", Market.Australia, "+61 412 345 678", "Mazda CX-5 1AB-2CD", "42 Harbour St, Sydney NSW 2000"),
            new ClaimantSpec("claimant.au2@example.com", "Liam O'Connor", Market.Australia, "+61 423 456 789", "Subaru Outback XYZ-987", "17 Collins St, Melbourne VIC 3000"),
            new ClaimantSpec("claimant.jp1@example.com", "Haruto Sato", Market.Japan, "+81 90 1234 5678", "Toyota Prius (Shinagawa 300 A 12-34)", "3-14-5 Shibuya, Tokyo 150-0002"),
            new ClaimantSpec("claimant.my1@example.com", "Nur Aisyah Rahman", Market.Malaysia, "+60 12 345 6789", "Proton X70 WXY 1234", "No. 8 Jalan Ampang, Kuala Lumpur 50450"),
            new ClaimantSpec("claimant.th1@example.com", "Somchai Prasert", Market.Thailand, "+66 81 234 5678", "Honda City 1AB 1234", "88 Sukhumvit Rd, Bangkok 10110")
        };

        var claimants = new List<(Claimant Claimant, User User, ClaimantSpec Spec, Policy Motor, Policy Property)>();
        var seq = 10000;
        foreach (var spec in specs)
        {
            var user = new User(spec.Email, hash, spec.Name, UserRole.Claimant, spec.Market, now);
            var claimant = new Claimant(user.Id, spec.Name, spec.Email, spec.Phone, spec.Market);
            var scale = Scale[spec.Market];
            var code = spec.Market.Code();
            var motor = new Policy($"{code}-MOT-{++seq}", spec.Email, ClaimType.Motor, spec.Market, 60_000m * scale,
                now.AddDays(-300), now.AddDays(65), spec.Vehicle, claimant.Id);
            var property = new Policy($"{code}-PRP-{++seq}", spec.Email, ClaimType.Property, spec.Market, 250_000m * scale,
                now.AddDays(-300), now.AddDays(65), spec.Address, claimant.Id);
            db.Users.Add(user);
            db.Claimants.Add(claimant);
            db.Policies.AddRange(motor, property);
            claimants.Add((claimant, user, spec, motor, property));
        }

        // Pre-issued policies for a customer who has not registered yet: register with this email to see them linked.
        db.Policies.Add(new Policy("SG-MOT-19999", "new.customer@example.com", ClaimType.Motor, Market.Singapore, 60_000m,
            now.AddDays(-200), now.AddDays(165), "Honda Vezel SGX9876Z"));
        db.Policies.Add(new Policy("SG-PRP-19998", "new.customer@example.com", ClaimType.Property, Market.Singapore, 250_000m,
            now.AddDays(-200), now.AddDays(165), "5 Bishan Street 11 #03-21, Singapore 570005"));

        var motorDescriptions = new[]
        {
            "Rear-end collision at a traffic light; bumper and boot damage.",
            "Side-swipe while changing lanes on the expressway; driver-side door and mirror damaged.",
            "Windscreen cracked by road debris and rear quarter panel scratched during heavy rain."
        };
        var propertyDescriptions = new[]
        {
            "Water damage from a burst pipe in the kitchen affecting flooring and cabinetry.",
            "Storm damage to balcony glazing and water ingress into the living room.",
            "Electrical fault caused a small fire in the study; smoke and fire damage to furniture."
        };

        const int total = 48;
        for (var i = 0; i < total; i++)
        {
            var c = claimants[i % claimants.Count];
            var isProperty = i % 3 == 0;
            var policy = isProperty ? c.Property : c.Motor;
            var scale = Scale[c.Spec.Market];
            var scenario = i % 8;

            var daysAgo = scenario switch
            {
                0 => rng.Next(1, 4),
                1 => rng.Next(2, 7),
                2 => rng.Next(5, 11),
                3 => rng.Next(20, 41),
                4 => rng.Next(25, 46),
                5 => rng.Next(15, 51),
                6 => rng.Next(15, 51),
                _ => rng.Next(3, 13)
            };
            var submittedAt = now.AddDays(-daysAgo).AddHours(-rng.Next(0, 10));
            var amount = isProperty ? rng.Next(30, 800) * 100m * scale : rng.Next(10, 200) * 100m * scale;
            var description = isProperty ? propertyDescriptions[rng.Next(3)] : motorDescriptions[rng.Next(3)];

            var claim = Claim.Submit(policy, c.Claimant.Id, submittedAt.AddDays(-rng.Next(1, 4)),
                isProperty ? c.Spec.Address : $"Near {c.Spec.Address.Split(',')[0]}", description, amount, c.User.Id,
                $"CLM-{c.Spec.Market.Code()}-{submittedAt:yyyyMMdd}-{200000 + i}", submittedAt);

            var marketOfficers = officers.Where(o => o.Market == c.Spec.Market).ToArray();
            var officer = marketOfficers[i % marketOfficers.Length];
            var t = submittedAt;

            if (scenario is 1 or 2 or 3 or 5 or 6 or 7)
            {
                t = t.AddHours(2 + rng.Next(0, 6));
                claim.AssignTo(officer.Id, officer.Id, t);
            }

            if (scenario == 2)
            {
                t = t.AddHours(4);
                claim.RequestInformation(officer.Id, "Please provide the police report and a repair or replacement estimate.", t.AddDays(5), t);
                if (i % 16 == 2)
                {
                    t = t.AddDays(1);
                    claim.ProvideInformation(claim.InformationRequests.First().Id,
                        "Attached the police report reference and the workshop estimate as requested.", c.User.Id, t);
                }
            }

            if (scenario is 5 or 6 or 7)
            {
                t = t.AddDays(1);
                var payout = Math.Round(amount * 0.8m, 2);
                claim.RecordAssessment(officer.Id, Math.Round(amount * 0.9m, 2), payout,
                    "Inspection complete; damage is consistent with the reported incident.", t);

                if (scenario == 5)
                    claim.Approve(officer.Id, payout, "Approved in line with the assessment.", policy.CoverageLimit, t.AddDays(1));
                else if (scenario == 6)
                    claim.Reject(officer.Id, "Loss falls under a policy exclusion (pre-existing damage).", t.AddDays(1));
            }

            db.Claims.Add(claim);
        }

        await db.SaveChangesAsync(ct);

        // Seed events are history, not news: do not relay them to Kafka.
        await db.OutboxMessages.Where(m => m.ProcessedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, now), ct);

        log.LogInformation("Seeded demo data: {Users} users, {Claims} claims. Demo password: {Password}",
            1 + officers.Length + specs.Length, total, DemoPassword);
    }
}
