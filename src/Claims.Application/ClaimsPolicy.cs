using Claims.Domain.Entities;
using Claims.Domain.Enums;

namespace Claims.Application;

/// <summary>Business constants kept in one place. Candidates for configuration in a real deployment.</summary>
public static class ClaimsPolicy
{
    /// <summary>Days from submission after which an open claim is overdue (clock pauses while awaiting the claimant).</summary>
    public const int SlaDays = 14;

    public const long MaxUploadBytes = 10 * 1024 * 1024;

    public static readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".docx" };

    public static bool IsOutstanding(ClaimStatus status) => status is not (ClaimStatus.Approved or ClaimStatus.Rejected);

    public static bool IsOverdue(Claim claim, DateTime now) =>
        (claim.Status is ClaimStatus.Submitted or ClaimStatus.UnderReview) &&
        claim.SubmittedAt < now.AddDays(-SlaDays);

    /// <summary>Indicative FX rates to USD for dashboard roll-ups only. A real system would use a rates service.</summary>
    private static readonly Dictionary<string, decimal> UsdRates = new()
    {
        ["SGD"] = 0.74m, ["HKD"] = 0.128m, ["AUD"] = 0.65m, ["JPY"] = 0.0067m, ["MYR"] = 0.22m, ["THB"] = 0.029m, ["USD"] = 1m
    };

    public static decimal ToUsd(string currency, decimal amount) =>
        UsdRates.TryGetValue(currency, out var rate) ? Math.Round(amount * rate, 2) : amount;
}
