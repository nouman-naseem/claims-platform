using Claims.Application.Contracts;
using Claims.Application.Exceptions;
using Claims.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Claims.Tests.Application;

public class ClaimServiceTests : IDisposable
{
    private readonly ServiceFixture _f = new();
    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Submit_persists_claim_and_outbox_message_in_the_same_commit()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1);

        await using var db = _f.NewDb();
        Assert.Equal(1, await db.Claims.CountAsync());
        var outbox = await db.OutboxMessages.SingleAsync();
        Assert.Equal("ClaimSubmitted", outbox.Type);
        Assert.Equal(claim.Id.ToString(), outbox.Key);
        Assert.Contains(claim.ClaimNumber, outbox.Payload);
        Assert.Null(outbox.ProcessedAt);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
    }

    [Fact]
    public async Task Claimant_cannot_read_another_claimants_claim()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1);
        await Assert.ThrowsAsync<NotFoundException>(() => _f.Claims(_f.Claimant2).GetAsync(claim.Id, default));
    }

    [Fact]
    public async Task Claimant_cannot_submit_against_someone_elses_policy()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _f.SubmitAsync(_f.Claimant1, _f.Policy2));
    }

    [Fact]
    public async Task Staff_cannot_submit_claims()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _f.SubmitAsync(_f.OfficerA, _f.Policy1));
    }

    [Fact]
    public async Task List_for_claimant_is_scoped_and_paginated()
    {
        for (var i = 0; i < 3; i++) await _f.SubmitAsync(_f.Claimant1, amount: 1_000m + i);
        await _f.SubmitAsync(_f.Claimant2);

        var page = await _f.Claims(_f.Claimant1).ListAsync(new ClaimQuery { Page = 1, PageSize = 2 }, default);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalPages);
        Assert.All(page.Items, i => Assert.Equal("Claimant One", i.ClaimantName));
    }

    [Fact]
    public async Task List_filters_by_status_and_unassigned()
    {
        var a = await _f.SubmitAsync(_f.Claimant1);
        await _f.SubmitAsync(_f.Claimant2);
        await _f.Claims(_f.OfficerA).AssignAsync(a.Id, new AssignClaimRequest(null), default);

        var unassigned = await _f.Claims(_f.OfficerA).ListAsync(new ClaimQuery { Unassigned = true }, default);
        var underReview = await _f.Claims(_f.OfficerA).ListAsync(new ClaimQuery { Status = ClaimStatus.UnderReview }, default);
        var mine = await _f.Claims(_f.OfficerA).ListAsync(new ClaimQuery { AssignedToMe = true }, default);

        Assert.Equal(1, unassigned.TotalCount);
        Assert.Equal(1, underReview.TotalCount);
        Assert.Equal(a.Id, Assert.Single(mine.Items).Id);
    }

    [Fact]
    public async Task Officer_cannot_decide_a_claim_that_is_not_assigned_to_them()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1);
        await _f.Claims(_f.OfficerA).AssignAsync(claim.Id, new AssignClaimRequest(null), default);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _f.Claims(_f.OfficerB).RejectAsync(claim.Id, new RejectClaimRequest("nope"), default));
    }

    [Fact]
    public async Task Officers_can_only_self_assign_but_managers_can_assign_anyone()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _f.Claims(_f.OfficerA).AssignAsync(claim.Id, new AssignClaimRequest(_f.OfficerB.UserId), default));

        var assigned = await _f.Claims(_f.Manager).AssignAsync(claim.Id, new AssignClaimRequest(_f.OfficerB.UserId), default);
        Assert.Equal(_f.OfficerB.UserId, assigned.AssignedOfficerId);
        Assert.Equal(ClaimStatus.UnderReview, assigned.Status);
    }

    [Fact]
    public async Task Full_lifecycle_emits_the_expected_events_and_hides_internal_notes_from_claimant()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1, amount: 8_000m);
        await _f.Claims(_f.OfficerA).AssignAsync(claim.Id, new AssignClaimRequest(null), default);
        var requested = await _f.Claims(_f.OfficerA).RequestInformationAsync(claim.Id,
            new RequestInformationRequest("Please send the police report.", TestData.Now.AddDays(5)), default);
        Assert.Equal(ClaimStatus.InformationRequested, requested.Status);

        var requestId = requested.InformationRequests.Single().Id;
        var answered = await _f.Claims(_f.Claimant1).ProvideInformationAsync(claim.Id, requestId,
            new ProvideInformationRequest("Report no. 12345 attached."), default);
        Assert.Equal(ClaimStatus.UnderReview, answered.Status);

        await _f.Claims(_f.OfficerA).AssessAsync(claim.Id, new AssessClaimRequest(7_500m, 6_500m, "Internal: minor fraud flag cleared."), default);
        var approved = await _f.Claims(_f.OfficerA).ApproveAsync(claim.Id, new ApproveClaimRequest(6_500m, "Approved."), default);

        Assert.Equal(ClaimStatus.Approved, approved.Status);
        Assert.Equal(6_500m, approved.Decision!.ApprovedAmount);

        var claimantView = await _f.Claims(_f.Claimant1).GetAsync(claim.Id, default);
        Assert.Empty(claimantView.Assessments);
        Assert.Null(claimantView.EstimatedExposure);
        Assert.DoesNotContain(claimantView.History, e => e.Type == ClaimEventType.AssessmentRecorded);
        Assert.Equal(ClaimStatus.Approved, claimantView.Status);

        await using var db = _f.NewDb();
        var types = await db.OutboxMessages.Select(m => m.Type).ToListAsync();
        Assert.Equal(new[]
        {
            "AdditionalInformationProvided", "AdditionalInformationRequested", "ClaimApproved", "ClaimAssigned", "ClaimSubmitted"
        }, types.OrderBy(t => t).ToArray());
    }

    [Fact]
    public async Task Approving_over_the_policy_limit_is_a_business_rule_violation()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1, amount: 5_000m);
        await _f.Claims(_f.OfficerA).AssignAsync(claim.Id, new AssignClaimRequest(null), default);
        await _f.Claims(_f.OfficerA).AssessAsync(claim.Id, new AssessClaimRequest(5_000m, 5_000m, "ok"), default);

        await Assert.ThrowsAsync<Claims.Domain.Common.DomainException>(() =>
            _f.Claims(_f.OfficerA).ApproveAsync(claim.Id, new ApproveClaimRequest(5_500m, null), default));
    }

    [Fact]
    public async Task Overdue_filter_uses_sla_and_pauses_while_waiting_for_the_claimant()
    {
        var late = TestData.Now.AddDays(20);
        var waiting = await _f.SubmitAsync(_f.Claimant1);
        await _f.Claims(_f.OfficerA).AssignAsync(waiting.Id, new AssignClaimRequest(null), default);
        await _f.Claims(_f.OfficerA).RequestInformationAsync(waiting.Id, new RequestInformationRequest("More info", null), default);
        var stale = await _f.SubmitAsync(_f.Claimant2);

        var overdue = await _f.Claims(_f.OfficerA, late).ListAsync(new ClaimQuery { Overdue = true }, default);

        Assert.Equal(stale.Id, Assert.Single(overdue.Items).Id);
        Assert.True(overdue.Items.Single().IsOverdue);
    }

    [Fact]
    public async Task Auto_assignment_picks_the_least_loaded_officer_and_is_idempotent()
    {
        var first = await _f.SubmitAsync(_f.Claimant1);
        await _f.Claims(_f.OfficerA).AssignAsync(first.Id, new AssignClaimRequest(null), default); // A now has load 1
        var second = await _f.SubmitAsync(_f.Claimant2);

        Assert.True(await _f.AutoAssign().TryAutoAssignAsync(second.Id, default));
        Assert.False(await _f.AutoAssign().TryAutoAssignAsync(second.Id, default)); // redelivery is a no-op

        var detail = await _f.Claims(_f.Manager).GetAsync(second.Id, default);
        Assert.Equal(_f.OfficerB.UserId, detail.AssignedOfficerId);
        Assert.Equal("System", detail.Assignments.Single().AssignedBy);
    }

    [Fact]
    public async Task Dashboard_is_manager_only_and_aggregates_correctly()
    {
        var a = await _f.SubmitAsync(_f.Claimant1, amount: 1_000m);
        await _f.SubmitAsync(_f.Claimant2, amount: 2_000m);
        await _f.Claims(_f.OfficerA).AssignAsync(a.Id, new AssignClaimRequest(null), default);

        await Assert.ThrowsAsync<ForbiddenException>(() => _f.Dashboard(_f.OfficerA).GetAsync(default));

        var d = await _f.Dashboard(_f.Manager).GetAsync(default);
        Assert.Equal(2, d.TotalClaims);
        Assert.Equal(2, d.OutstandingClaims);
        Assert.Equal(1, d.PendingDecisions);
        Assert.Equal(1, d.UnassignedClaims);
        Assert.Equal(3_000m * 0.74m, d.EstimatedExposureUsd);
        Assert.Equal(1, d.TeamWorkload.Single(w => w.OfficerId == _f.OfficerA.UserId).OpenClaims);
    }

    [Fact]
    public async Task Upload_accepts_valid_pdf_and_stores_the_file()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1);
        await using var content = new MemoryStream("%PDF-1.4 test"u8.ToArray());

        var doc = await _f.Claims(_f.Claimant1).UploadDocumentAsync(claim.Id, "report.pdf", content, default);

        Assert.Equal("report.pdf", doc.FileName);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Single(_f.Storage.Files);

        var download = await _f.Claims(_f.Claimant1).DownloadDocumentAsync(claim.Id, doc.Id, default);
        Assert.Equal("report.pdf", download.FileName);
        await Assert.ThrowsAsync<NotFoundException>(() => _f.Claims(_f.Claimant2).DownloadDocumentAsync(claim.Id, doc.Id, default));
    }

    [Theory]
    [InlineData("malware.exe", "MZ-binary")]
    [InlineData("fake.pdf", "this is not a pdf")]
    public async Task Upload_rejects_bad_extension_or_mismatched_content(string fileName, string text)
    {
        var claim = await _f.SubmitAsync(_f.Claimant1);
        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _f.Claims(_f.Claimant1).UploadDocumentAsync(claim.Id, fileName, content, default));
        Assert.Empty(_f.Storage.Files);
    }

    [Fact]
    public async Task Concurrent_modification_is_detected_by_the_version_token()
    {
        var claim = await _f.SubmitAsync(_f.Claimant1);

        // Two officers load the same version and both try to assign: the second save must lose.
        await using var db1 = _f.NewDb();
        await using var db2 = _f.NewDb();
        var c1 = await db1.Claims.Include(c => c.Assignments).SingleAsync(c => c.Id == claim.Id);
        var c2 = await db2.Claims.Include(c => c.Assignments).SingleAsync(c => c.Id == claim.Id);
        c1.AssignTo(_f.OfficerA.UserId, _f.OfficerA.UserId, TestData.Now);
        c2.AssignTo(_f.OfficerB.UserId, _f.OfficerB.UserId, TestData.Now);

        await db1.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
}
