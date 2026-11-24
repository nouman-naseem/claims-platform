using Claims.Domain.Enums;

namespace Claims.Application.Contracts;

// ---- Requests ----
public record SubmitClaimRequest(Guid PolicyId, DateTime IncidentDate, string IncidentLocation, string Description, decimal ClaimedAmount);
public record AssignClaimRequest(Guid? OfficerId);
public record RequestInformationRequest(string Message, DateTime? DueDate);
public record ProvideInformationRequest(string Response);
public record AssessClaimRequest(decimal EstimatedLoss, decimal RecommendedPayout, string Notes);
public record ApproveClaimRequest(decimal ApprovedAmount, string? Notes);
public record RejectClaimRequest(string Reason);

public class ClaimQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public ClaimStatus? Status { get; set; }
    public ClaimType? Type { get; set; }
    public Market? Market { get; set; }
    public bool? Unassigned { get; set; }
    public bool? AssignedToMe { get; set; }
    public Guid? AssignedOfficerId { get; set; }
    public bool? Overdue { get; set; }
    public string? Search { get; set; }
}

// ---- Responses ----
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record ClaimSummaryDto(Guid Id, string ClaimNumber, ClaimType Type, Market Market, ClaimStatus Status,
    string Currency, decimal ClaimedAmount, DateTime IncidentDate, DateTime SubmittedAt, DateTime UpdatedAt,
    string ClaimantName, string? AssignedOfficerName, bool IsOverdue);

public record ClaimantDto(Guid Id, string FullName, string Email, string Phone, Market Market);
public record DocumentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string UploadedBy, DateTime UploadedAt);
public record InformationRequestDto(Guid Id, string Message, string RequestedBy, DateTime RequestedAt, DateTime? DueDate,
    string? Response, DateTime? RespondedAt, bool IsOpen);
public record AssessmentDto(Guid Id, string Officer, decimal EstimatedLoss, decimal RecommendedPayout, string Notes, DateTime AssessedAt);
public record AssignmentDto(Guid Id, Guid OfficerId, string Officer, string AssignedBy, DateTime AssignedAt, DateTime? UnassignedAt);
public record DecisionDto(DecisionOutcome Outcome, decimal? ApprovedAmount, string Currency, string Reason, string DecidedBy, DateTime DecidedAt);
public record ClaimEventDto(ClaimEventType Type, string Actor, string Description, DateTime OccurredAt);

public record ClaimDetailDto(
    Guid Id, string ClaimNumber, ClaimType Type, Market Market, ClaimStatus Status, string Currency,
    decimal ClaimedAmount, decimal? EstimatedExposure, DateTime IncidentDate, string IncidentLocation, string Description,
    DateTime SubmittedAt, DateTime UpdatedAt, bool IsOverdue, Guid? AssignedOfficerId, string? AssignedOfficerName,
    PolicyDto Policy, ClaimantDto Claimant,
    IReadOnlyList<DocumentDto> Documents, IReadOnlyList<InformationRequestDto> InformationRequests,
    IReadOnlyList<AssessmentDto> Assessments, IReadOnlyList<AssignmentDto> Assignments,
    DecisionDto? Decision, IReadOnlyList<ClaimEventDto> History);

public sealed record DocumentDownload(Stream Content, string FileName, string ContentType);

// ---- Dashboard ----
public record CountByDto(string Key, int Count);
public record ExposureByMarketDto(string Market, string Currency, decimal ExposureLocal, decimal ExposureUsd);
public record OfficerWorkloadDto(Guid OfficerId, string Name, string Market, int OpenClaims, int OverdueClaims,
    int DecidedClaims, int ApprovedClaims, double AvgDaysToDecision);

public record DashboardDto(
    int TotalClaims, int OutstandingClaims, int PendingDecisions, int UnassignedClaims, int OverdueClaims,
    decimal EstimatedExposureUsd, int SlaDays,
    IReadOnlyList<CountByDto> ByStatus, IReadOnlyList<CountByDto> ByType, IReadOnlyList<CountByDto> ByMarket,
    IReadOnlyList<ExposureByMarketDto> ExposureByMarket, IReadOnlyList<OfficerWorkloadDto> TeamWorkload);
