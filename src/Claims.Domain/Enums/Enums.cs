namespace Claims.Domain.Enums;

public enum UserRole { Claimant, ClaimsOfficer, Manager }

public enum Market { Singapore, HongKong, Australia, Japan, Malaysia, Thailand }

public enum ClaimType { Motor, Property }

/// <summary>Claim lifecycle states. Valid transitions are defined in <see cref="Entities.Claim"/>.</summary>
public enum ClaimStatus { Submitted, UnderReview, InformationRequested, Approved, Rejected }

public enum ClaimEventType
{
    ClaimSubmitted,
    ClaimAssigned,
    AdditionalInformationRequested,
    AdditionalInformationProvided,
    DocumentUploaded,
    AssessmentRecorded,
    ClaimApproved,
    ClaimRejected
}

public enum DecisionOutcome { Approved, Rejected }

public static class MarketExtensions
{
    public static string Code(this Market market) => market switch
    {
        Market.Singapore => "SG",
        Market.HongKong => "HK",
        Market.Australia => "AU",
        Market.Japan => "JP",
        Market.Malaysia => "MY",
        Market.Thailand => "TH",
        _ => throw new ArgumentOutOfRangeException(nameof(market))
    };

    public static string Currency(this Market market) => market switch
    {
        Market.Singapore => "SGD",
        Market.HongKong => "HKD",
        Market.Australia => "AUD",
        Market.Japan => "JPY",
        Market.Malaysia => "MYR",
        Market.Thailand => "THB",
        _ => throw new ArgumentOutOfRangeException(nameof(market))
    };
}
