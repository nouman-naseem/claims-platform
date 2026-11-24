using Claims.Domain.Common;
using Claims.Domain.Enums;

namespace Claims.Domain.Entities;

public class ClaimDocument : Entity
{
    private ClaimDocument() { }

    public ClaimDocument(Guid id, Guid claimId, string fileName, string storageKey, string contentType,
        long sizeBytes, Guid uploadedByUserId, DateTime now)
    {
        Id = id;
        ClaimId = claimId;
        FileName = fileName;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = now;
    }

    public Guid ClaimId { get; private set; }
    public string FileName { get; private set; } = default!;
    public string StorageKey { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public DateTime UploadedAt { get; private set; }
}

public class Assessment : Entity
{
    private Assessment() { }

    public Assessment(Guid claimId, Guid officerId, decimal estimatedLoss, decimal recommendedPayout, string notes, DateTime now)
    {
        ClaimId = claimId;
        OfficerId = officerId;
        EstimatedLoss = estimatedLoss;
        RecommendedPayout = recommendedPayout;
        Notes = notes;
        AssessedAt = now;
    }

    public Guid ClaimId { get; private set; }
    public Guid OfficerId { get; private set; }
    public decimal EstimatedLoss { get; private set; }
    public decimal RecommendedPayout { get; private set; }
    public string Notes { get; private set; } = default!;
    public DateTime AssessedAt { get; private set; }
}

public class Assignment : Entity
{
    private Assignment() { }

    public Assignment(Guid claimId, Guid officerId, Guid assignedByUserId, DateTime now)
    {
        ClaimId = claimId;
        OfficerId = officerId;
        AssignedByUserId = assignedByUserId;
        AssignedAt = now;
    }

    public Guid ClaimId { get; private set; }
    public Guid OfficerId { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public DateTime AssignedAt { get; private set; }
    public DateTime? UnassignedAt { get; private set; }

    public void Close(DateTime now) => UnassignedAt = now;
}

public class InformationRequest : Entity
{
    private InformationRequest() { }

    public InformationRequest(Guid claimId, Guid requestedByUserId, string message, DateTime? dueDate, DateTime now)
    {
        ClaimId = claimId;
        RequestedByUserId = requestedByUserId;
        Message = message;
        DueDate = dueDate;
        RequestedAt = now;
    }

    public Guid ClaimId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string Message { get; private set; } = default!;
    public DateTime RequestedAt { get; private set; }
    public DateTime? DueDate { get; private set; }
    public string? Response { get; private set; }
    public DateTime? RespondedAt { get; private set; }

    public bool IsOpen => RespondedAt is null;

    public void Respond(string response, DateTime now)
    {
        if (!IsOpen) throw new DomainException("This information request has already been answered.");
        Response = response;
        RespondedAt = now;
    }
}

public class Decision : Entity
{
    private Decision() { }

    public Decision(Guid claimId, DecisionOutcome outcome, decimal? approvedAmount, string reason, Guid decidedByUserId, DateTime now)
    {
        ClaimId = claimId;
        Outcome = outcome;
        ApprovedAmount = approvedAmount;
        Reason = reason;
        DecidedByUserId = decidedByUserId;
        DecidedAt = now;
    }

    public Guid ClaimId { get; private set; }
    public DecisionOutcome Outcome { get; private set; }
    /// <summary>Settlement amount in the claim's currency (approvals only).</summary>
    public decimal? ApprovedAmount { get; private set; }
    public string Reason { get; private set; } = default!;
    public Guid DecidedByUserId { get; private set; }
    public DateTime DecidedAt { get; private set; }
}

/// <summary>Immutable audit trail entry. Every state change on a claim writes one.</summary>
public class ClaimEvent : Entity
{
    private ClaimEvent() { }

    public ClaimEvent(Guid claimId, ClaimEventType type, Guid? actorUserId, string description, DateTime now)
    {
        ClaimId = claimId;
        Type = type;
        ActorUserId = actorUserId;
        Description = description;
        OccurredAt = now;
    }

    public Guid ClaimId { get; private set; }
    public ClaimEventType Type { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Description { get; private set; } = default!;
    public DateTime OccurredAt { get; private set; }
}
