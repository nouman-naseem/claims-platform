using Claims.Domain.Common;
using Claims.Domain.Enums;

namespace Claims.Domain.Entities;

/// <summary>
/// Claim aggregate root. All lifecycle changes go through its methods so invariants and the
/// audit trail cannot be bypassed. Time is always passed in (no hidden clock) to keep it testable.
/// </summary>
public class Claim : Entity
{
    private static readonly Dictionary<ClaimStatus, ClaimStatus[]> Transitions = new()
    {
        [ClaimStatus.Submitted] = new[] { ClaimStatus.UnderReview },
        [ClaimStatus.UnderReview] = new[] { ClaimStatus.InformationRequested, ClaimStatus.Approved, ClaimStatus.Rejected },
        [ClaimStatus.InformationRequested] = new[] { ClaimStatus.UnderReview },
        [ClaimStatus.Approved] = Array.Empty<ClaimStatus>(),
        [ClaimStatus.Rejected] = Array.Empty<ClaimStatus>()
    };

    private readonly List<ClaimDocument> _documents = new();
    private readonly List<Assessment> _assessments = new();
    private readonly List<Assignment> _assignments = new();
    private readonly List<InformationRequest> _informationRequests = new();
    private readonly List<ClaimEvent> _events = new();
    private readonly List<DomainEvent> _domainEvents = new();

    private Claim() { }

    public static bool CanTransition(ClaimStatus from, ClaimStatus to) => Transitions[from].Contains(to);

    public string ClaimNumber { get; private set; } = default!;
    public Guid PolicyId { get; private set; }
    public Guid ClaimantId { get; private set; }
    public ClaimType Type { get; private set; }
    public Market Market { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTime IncidentDate { get; private set; }
    public string IncidentLocation { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public decimal ClaimedAmount { get; private set; }
    /// <summary>Outstanding liability estimate: claimed amount, then the latest assessment's payout, zero once decided.</summary>
    public decimal EstimatedExposure { get; private set; }
    public ClaimStatus Status { get; private set; }
    public Guid? AssignedOfficerId { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    /// <summary>Optimistic concurrency token, incremented on every mutation.</summary>
    public int Version { get; private set; }

    public Policy Policy { get; private set; } = default!;
    public Claimant Claimant { get; private set; } = default!;
    public User? AssignedOfficer { get; private set; }
    public Decision? Decision { get; private set; }

    public IReadOnlyCollection<ClaimDocument> Documents => _documents;
    public IReadOnlyCollection<Assessment> Assessments => _assessments;
    public IReadOnlyCollection<Assignment> Assignments => _assignments;
    public IReadOnlyCollection<InformationRequest> InformationRequests => _informationRequests;
    public IReadOnlyCollection<ClaimEvent> Events => _events;
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents;

    public bool IsDecided => Status is ClaimStatus.Approved or ClaimStatus.Rejected;

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Claim Submit(Policy policy, Guid claimantId, DateTime incidentDate, string incidentLocation,
        string description, decimal claimedAmount, Guid actorUserId, string claimNumber, DateTime now)
    {
        if (policy.ClaimantId != claimantId) throw new DomainException("The policy does not belong to this claimant.");
        if (incidentDate > now) throw new DomainException("The incident date cannot be in the future.");
        if (!policy.IsActiveOn(incidentDate)) throw new DomainException("The policy was not active on the incident date.");
        if (claimedAmount <= 0) throw new DomainException("The claimed amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("A description is required.");
        if (string.IsNullOrWhiteSpace(incidentLocation)) throw new DomainException("An incident location is required.");

        var claim = new Claim
        {
            ClaimNumber = claimNumber,
            PolicyId = policy.Id,
            ClaimantId = claimantId,
            Type = policy.Type,
            Market = policy.Market,
            Currency = policy.Market.Currency(),
            IncidentDate = incidentDate,
            IncidentLocation = incidentLocation.Trim(),
            Description = description.Trim(),
            ClaimedAmount = claimedAmount,
            EstimatedExposure = claimedAmount,
            Status = ClaimStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        claim.Record(ClaimEventType.ClaimSubmitted, actorUserId,
            $"Claim submitted for {claimedAmount:N2} {claim.Currency}.", now, publish: true,
            new Dictionary<string, object?>
            {
                ["type"] = claim.Type.ToString(),
                ["market"] = claim.Market.ToString(),
                ["claimedAmount"] = claimedAmount,
                ["currency"] = claim.Currency
            });
        return claim;
    }

    public void AssignTo(Guid officerId, Guid assignedByUserId, DateTime now)
    {
        if (IsDecided) throw new DomainException("A decided claim cannot be assigned.");
        if (AssignedOfficerId == officerId) throw new DomainException("The claim is already assigned to this officer.");

        _assignments.FirstOrDefault(a => a.UnassignedAt is null)?.Close(now);
        _assignments.Add(new Assignment(Id, officerId, assignedByUserId, now));
        AssignedOfficerId = officerId;

        if (Status == ClaimStatus.Submitted) TransitionTo(ClaimStatus.UnderReview);
        Touch(now);
        Record(ClaimEventType.ClaimAssigned, assignedByUserId, "Claim assigned to a claims officer.", now, true,
            new Dictionary<string, object?> { ["officerId"] = officerId });
    }

    public void RequestInformation(Guid officerId, string message, DateTime? dueDate, DateTime now)
    {
        EnsureAssignedTo(officerId);
        if (string.IsNullOrWhiteSpace(message)) throw new DomainException("A message is required.");
        TransitionTo(ClaimStatus.InformationRequested);
        _informationRequests.Add(new InformationRequest(Id, officerId, message.Trim(), dueDate, now));
        Touch(now);
        Record(ClaimEventType.AdditionalInformationRequested, officerId, "Additional information requested from the claimant.", now, true,
            new Dictionary<string, object?> { ["dueDate"] = dueDate });
    }

    public void ProvideInformation(Guid requestId, string response, Guid actorUserId, DateTime now)
    {
        var request = _informationRequests.FirstOrDefault(r => r.Id == requestId)
                      ?? throw new DomainException("Information request not found on this claim.");
        if (string.IsNullOrWhiteSpace(response)) throw new DomainException("A response is required.");
        TransitionTo(ClaimStatus.UnderReview);
        request.Respond(response.Trim(), now);
        Touch(now);
        Record(ClaimEventType.AdditionalInformationProvided, actorUserId, "Claimant provided the requested information.", now, true);
    }

    public void AddDocument(ClaimDocument document, Guid actorUserId, DateTime now)
    {
        if (IsDecided) throw new DomainException("Documents cannot be added to a decided claim.");
        _documents.Add(document);
        Touch(now);
        Record(ClaimEventType.DocumentUploaded, actorUserId, $"Document uploaded: {document.FileName}.", now, publish: false);
    }

    public void RecordAssessment(Guid officerId, decimal estimatedLoss, decimal recommendedPayout, string notes, DateTime now)
    {
        EnsureAssignedTo(officerId);
        if (Status != ClaimStatus.UnderReview) throw new DomainException("Claims can only be assessed while under review.");
        if (estimatedLoss < 0 || recommendedPayout < 0) throw new DomainException("Assessment amounts cannot be negative.");
        _assessments.Add(new Assessment(Id, officerId, estimatedLoss, recommendedPayout, notes?.Trim() ?? string.Empty, now));
        EstimatedExposure = recommendedPayout;
        Touch(now);
        Record(ClaimEventType.AssessmentRecorded, officerId, "Assessment recorded.", now, publish: false);
    }

    public void Approve(Guid officerId, decimal approvedAmount, string? notes, decimal coverageLimit, DateTime now)
    {
        EnsureAssignedTo(officerId);
        if (!_assessments.Any()) throw new DomainException("A claim must be assessed before a decision is recorded.");
        if (approvedAmount <= 0) throw new DomainException("The approved amount must be greater than zero.");
        if (approvedAmount > ClaimedAmount) throw new DomainException("The approved amount cannot exceed the claimed amount.");
        if (approvedAmount > coverageLimit)
            throw new DomainException($"The approved amount exceeds the policy coverage limit of {coverageLimit:N2}.");

        TransitionTo(ClaimStatus.Approved);
        Decision = new Decision(Id, DecisionOutcome.Approved, approvedAmount, notes?.Trim() ?? string.Empty, officerId, now);
        EstimatedExposure = 0;
        Touch(now);
        Record(ClaimEventType.ClaimApproved, officerId, $"Claim approved for settlement of {approvedAmount:N2} {Currency}.", now, true,
            new Dictionary<string, object?> { ["approvedAmount"] = approvedAmount, ["currency"] = Currency });
    }

    public void Reject(Guid officerId, string reason, DateTime now)
    {
        EnsureAssignedTo(officerId);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A rejection reason is required.");
        if (!_assessments.Any()) throw new DomainException("A claim must be assessed before a decision is recorded.");

        TransitionTo(ClaimStatus.Rejected);
        Decision = new Decision(Id, DecisionOutcome.Rejected, null, reason.Trim(), officerId, now);
        EstimatedExposure = 0;
        Touch(now);
        Record(ClaimEventType.ClaimRejected, officerId, "Claim rejected.", now, true,
            new Dictionary<string, object?> { ["reason"] = reason.Trim() });
    }

    private void EnsureAssignedTo(Guid officerId)
    {
        if (AssignedOfficerId != officerId)
            throw new DomainException("Only the officer assigned to this claim can perform this action.");
    }

    private void TransitionTo(ClaimStatus next)
    {
        if (!CanTransition(Status, next)) throw new InvalidStateTransitionException(Status, next);
        Status = next;
    }

    private void Touch(DateTime now)
    {
        UpdatedAt = now;
        Version++;
    }

    private void Record(ClaimEventType type, Guid? actorUserId, string description, DateTime now, bool publish,
        Dictionary<string, object?>? data = null)
    {
        _events.Add(new ClaimEvent(Id, type, actorUserId, description, now));
        if (publish)
            _domainEvents.Add(new DomainEvent(type.ToString(), Id, ClaimNumber, now,
                data ?? new Dictionary<string, object?>()));
    }
}
