using Claims.Domain.Common;
using Claims.Domain.Entities;
using Claims.Domain.Enums;

namespace Claims.Tests.Domain;

public class ClaimLifecycleTests
{
    private static readonly Guid Officer = Guid.NewGuid();
    private static DateTime T(int hours) => TestData.Now.AddHours(hours);

    private static Claim AssignedClaim(decimal amount = 5_000m)
    {
        var claim = TestData.NewClaim(amount: amount);
        claim.AssignTo(Officer, Officer, T(1));
        return claim;
    }

    private static Claim AssessedClaim(decimal amount = 5_000m)
    {
        var claim = AssignedClaim(amount);
        claim.RecordAssessment(Officer, amount, amount * 0.8m, "ok", T(2));
        return claim;
    }

    [Fact]
    public void Submit_creates_submitted_claim_with_audit_event_and_domain_event()
    {
        var claim = TestData.NewClaim();

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal("SGD", claim.Currency);
        Assert.Single(claim.Events);
        Assert.Equal(ClaimEventType.ClaimSubmitted, claim.Events.Single().Type);
        Assert.Equal("ClaimSubmitted", claim.DomainEvents.Single().EventType);
        Assert.Equal(claim.ClaimedAmount, claim.EstimatedExposure);
    }

    [Fact]
    public void Submit_rejects_incident_outside_policy_period()
    {
        var cid = Guid.NewGuid();
        var policy = TestData.NewPolicy(cid);
        var ex = Assert.Throws<DomainException>(() => Claim.Submit(policy, cid, TestData.Now.AddDays(-400), "x", "Some description",
            100, Guid.NewGuid(), "N1", TestData.Now));
        Assert.Contains("not active", ex.Message);
    }

    [Fact]
    public void Submit_rejects_future_incident_date()
    {
        var cid = Guid.NewGuid();
        var policy = TestData.NewPolicy(cid);
        Assert.Throws<DomainException>(() => Claim.Submit(policy, cid, TestData.Now.AddDays(1), "x", "Some description",
            100, Guid.NewGuid(), "N1", TestData.Now));
    }

    [Fact]
    public void Submit_rejects_policy_owned_by_someone_else()
    {
        var policy = TestData.NewPolicy(Guid.NewGuid());
        Assert.Throws<DomainException>(() => Claim.Submit(policy, Guid.NewGuid(), TestData.Now.AddDays(-1), "x", "Some description",
            100, Guid.NewGuid(), "N1", TestData.Now));
    }

    [Fact]
    public void Submit_rejects_non_positive_amount()
    {
        var cid = Guid.NewGuid();
        var policy = TestData.NewPolicy(cid);
        Assert.Throws<DomainException>(() => Claim.Submit(policy, cid, TestData.Now.AddDays(-1), "x", "Some description",
            0, Guid.NewGuid(), "N1", TestData.Now));
    }

    [Fact]
    public void Assign_moves_submitted_claim_under_review_and_records_assignment()
    {
        var claim = TestData.NewClaim();
        claim.AssignTo(Officer, Officer, T(1));

        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal(Officer, claim.AssignedOfficerId);
        Assert.Single(claim.Assignments);
        Assert.Contains(claim.DomainEvents, e => e.EventType == "ClaimAssigned");
    }

    [Fact]
    public void Reassign_closes_previous_assignment()
    {
        var claim = AssignedClaim();
        var other = Guid.NewGuid();
        claim.AssignTo(other, Guid.NewGuid(), T(3));

        Assert.Equal(2, claim.Assignments.Count);
        Assert.Single(claim.Assignments, a => a.UnassignedAt is null);
        Assert.Equal(other, claim.AssignedOfficerId);
    }

    [Fact]
    public void Assign_same_officer_twice_is_rejected()
    {
        var claim = AssignedClaim();
        Assert.Throws<DomainException>(() => claim.AssignTo(Officer, Officer, T(2)));
    }

    [Fact]
    public void Request_then_provide_information_returns_claim_to_review()
    {
        var claim = AssignedClaim();
        claim.RequestInformation(Officer, "Need the police report", T(100), T(2));
        Assert.Equal(ClaimStatus.InformationRequested, claim.Status);

        var request = claim.InformationRequests.Single();
        Assert.True(request.IsOpen);

        claim.ProvideInformation(request.Id, "Report attached", Guid.NewGuid(), T(3));
        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.False(request.IsOpen);
    }

    [Fact]
    public void Cannot_request_information_twice_in_a_row()
    {
        var claim = AssignedClaim();
        claim.RequestInformation(Officer, "First", null, T(2));
        Assert.Throws<InvalidStateTransitionException>(() => claim.RequestInformation(Officer, "Second", null, T(3)));
    }

    [Fact]
    public void Approve_without_assessment_is_rejected()
    {
        var claim = AssignedClaim();
        var ex = Assert.Throws<DomainException>(() => claim.Approve(Officer, 1_000m, null, 60_000m, T(2)));
        Assert.Contains("assessed", ex.Message);
    }

    [Fact]
    public void Approve_above_coverage_limit_is_rejected()
    {
        var claim = AssessedClaim(amount: 5_000m);
        Assert.Throws<DomainException>(() => claim.Approve(Officer, 4_000m, null, coverageLimit: 3_000m, T(3)));
    }

    [Fact]
    public void Approve_above_claimed_amount_is_rejected()
    {
        var claim = AssessedClaim(amount: 5_000m);
        Assert.Throws<DomainException>(() => claim.Approve(Officer, 5_001m, null, 60_000m, T(3)));
    }

    [Fact]
    public void Approve_by_unassigned_officer_is_rejected()
    {
        var claim = AssessedClaim();
        Assert.Throws<DomainException>(() => claim.Approve(Guid.NewGuid(), 1_000m, null, 60_000m, T(3)));
    }

    [Fact]
    public void Approve_records_decision_zeroes_exposure_and_raises_event()
    {
        var claim = AssessedClaim();
        claim.Approve(Officer, 3_500m, "Fine", 60_000m, T(3));

        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(3_500m, claim.Decision!.ApprovedAmount);
        Assert.Equal(0m, claim.EstimatedExposure);
        Assert.Contains(claim.DomainEvents, e => e.EventType == "ClaimApproved");
    }

    [Fact]
    public void Reject_requires_reason()
    {
        var claim = AssessedClaim();
        Assert.Throws<DomainException>(() => claim.Reject(Officer, "  ", T(3)));
    }

    [Fact]
    public void Reject_records_decision_and_event()
    {
        var claim = AssessedClaim();
        claim.Reject(Officer, "Excluded peril", T(3));

        Assert.Equal(ClaimStatus.Rejected, claim.Status);
        Assert.Equal(DecisionOutcome.Rejected, claim.Decision!.Outcome);
        Assert.Contains(claim.DomainEvents, e => e.EventType == "ClaimRejected");
    }

    [Fact]
    public void Decided_claim_is_final()
    {
        var claim = AssessedClaim();
        claim.Reject(Officer, "Excluded peril", T(3));

        Assert.Throws<DomainException>(() => claim.AssignTo(Guid.NewGuid(), Officer, T(4)));
        Assert.Throws<InvalidStateTransitionException>(() => claim.Approve(Officer, 100m, null, 60_000m, T(4)));
        Assert.Throws<DomainException>(() => claim.AddDocument(
            new ClaimDocument(Guid.NewGuid(), claim.Id, "a.pdf", "k", "application/pdf", 10, Officer, T(4)), Officer, T(4)));
    }

    [Fact]
    public void Document_upload_is_audited_but_not_published()
    {
        var claim = TestData.NewClaim();
        claim.ClearDomainEvents();
        claim.AddDocument(new ClaimDocument(Guid.NewGuid(), claim.Id, "photo.png", "k", "image/png", 10, Officer, T(1)), Officer, T(1));

        Assert.Contains(claim.Events, e => e.Type == ClaimEventType.DocumentUploaded);
        Assert.Empty(claim.DomainEvents);
    }

    [Fact]
    public void Every_mutation_increments_version()
    {
        var claim = TestData.NewClaim();
        var v0 = claim.Version;
        claim.AssignTo(Officer, Officer, T(1));
        Assert.True(claim.Version > v0);
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.UnderReview, true)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Rejected, false)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.InformationRequested, true)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Approved, true)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Rejected, true)]
    [InlineData(ClaimStatus.InformationRequested, ClaimStatus.UnderReview, true)]
    [InlineData(ClaimStatus.InformationRequested, ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.UnderReview, false)]
    [InlineData(ClaimStatus.Rejected, ClaimStatus.UnderReview, false)]
    public void Transition_table_is_enforced(ClaimStatus from, ClaimStatus to, bool allowed) =>
        Assert.Equal(allowed, Claim.CanTransition(from, to));
}
