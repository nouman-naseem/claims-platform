using System.Security.Cryptography;
using Claims.Application.Abstractions;
using Claims.Application.Contracts;
using Claims.Application.Exceptions;
using Claims.Domain.Entities;
using Claims.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Claims.Application.Services;

/// <summary>
/// Orchestrates claim use cases: authorisation, loading, calling the aggregate, persisting.
/// Business rules live in the <see cref="Claim"/> aggregate, not here.
/// </summary>
public sealed class ClaimService
{
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    private readonly IClaimsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;
    private readonly IFileStorage _storage;
    private readonly ILogger<ClaimService> _log;

    public ClaimService(IClaimsDbContext db, ICurrentUser user, TimeProvider clock, IFileStorage storage, ILogger<ClaimService> log)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _storage = storage;
        _log = log;
    }

    private bool IsClaimant => _user.Role == UserRole.Claimant;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ queries

    public async Task<PagedResult<ClaimSummaryDto>> ListAsync(ClaimQuery q, CancellationToken ct)
    {
        var cutoff = Now.AddDays(-ClaimsPolicy.SlaDays);
        IQueryable<Claim> query = _db.Claims.AsNoTracking();

        if (IsClaimant)
        {
            var claimantId = _user.ClaimantId ?? throw new ForbiddenException();
            query = query.Where(c => c.ClaimantId == claimantId);
        }

        if (q.Status is { } status) query = query.Where(c => c.Status == status);
        if (q.Type is { } type) query = query.Where(c => c.Type == type);
        if (q.Market is { } market) query = query.Where(c => c.Market == market);
        if (q.Unassigned == true) query = query.Where(c => c.AssignedOfficerId == null);
        if (q.AssignedOfficerId is { } officerId && !IsClaimant) query = query.Where(c => c.AssignedOfficerId == officerId);
        if (q.AssignedToMe == true && !IsClaimant)
        {
            var me = _user.UserId;
            query = query.Where(c => c.AssignedOfficerId == me);
        }
        if (q.Overdue == true)
            query = query.Where(c => (c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview) && c.SubmittedAt < cutoff);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            query = query.Where(c => c.ClaimNumber.Contains(term) || c.Claimant.FullName.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.SubmittedAt).ThenBy(c => c.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(c => new ClaimSummaryDto(
                c.Id, c.ClaimNumber, c.Type, c.Market, c.Status, c.Currency, c.ClaimedAmount,
                c.IncidentDate, c.SubmittedAt, c.UpdatedAt, c.Claimant.FullName,
                c.AssignedOfficer != null ? c.AssignedOfficer.FullName : null,
                (c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview) && c.SubmittedAt < cutoff))
            .ToListAsync(ct);

        return new PagedResult<ClaimSummaryDto>(items, q.Page, q.PageSize, total);
    }

    public async Task<ClaimDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var claim = await LoadAsync(id, track: false, ct);
        return await MapDetailAsync(claim, ct);
    }

    // ------------------------------------------------------------------ claimant commands

    public async Task<ClaimDetailDto> SubmitAsync(SubmitClaimRequest r, CancellationToken ct)
    {
        if (!IsClaimant) throw new ForbiddenException("Only claimants can report claims.");
        var claimantId = _user.ClaimantId ?? throw new ForbiddenException();

        var policy = await _db.Policies.FirstOrDefaultAsync(p => p.Id == r.PolicyId && p.ClaimantId == claimantId, ct)
                     ?? throw new NotFoundException("Policy", r.PolicyId);

        var now = Now;
        var number = $"CLM-{policy.Market.Code()}-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 1000000)}";
        var claim = Claim.Submit(policy, claimantId, ToUtc(r.IncidentDate), r.IncidentLocation, r.Description,
            r.ClaimedAmount, _user.UserId, number, now);

        _db.Claims.Add(claim);
        await _db.SaveChangesAsync(ct); // claim + audit event + outbox message commit atomically
        _log.LogInformation("Claim {ClaimNumber} submitted by claimant {ClaimantId}", number, claimantId);
        return await GetAsync(claim.Id, ct);
    }

    public async Task<ClaimDetailDto> ProvideInformationAsync(Guid claimId, Guid requestId, ProvideInformationRequest r, CancellationToken ct)
    {
        if (!IsClaimant) throw new ForbiddenException();
        var claim = await LoadAsync(claimId, track: true, ct);
        claim.ProvideInformation(requestId, r.Response, _user.UserId, Now);
        await _db.SaveChangesAsync(ct);
        return await MapDetailAsync(claim, ct);
    }

    // ------------------------------------------------------------------ officer / manager commands

    public async Task<ClaimDetailDto> AssignAsync(Guid claimId, AssignClaimRequest r, CancellationToken ct)
    {
        if (IsClaimant) throw new ForbiddenException();

        Guid officerId;
        if (_user.Role == UserRole.ClaimsOfficer)
        {
            if (r.OfficerId is { } requested && requested != _user.UserId)
                throw new ForbiddenException("Officers can only assign claims to themselves.");
            officerId = _user.UserId;
        }
        else
        {
            officerId = r.OfficerId ?? throw new Domain.Common.DomainException("OfficerId is required when a manager assigns a claim.");
        }

        var officerExists = await _db.Users.AnyAsync(u => u.Id == officerId && u.Role == UserRole.ClaimsOfficer && u.IsActive, ct);
        if (!officerExists) throw new NotFoundException("Officer", officerId);

        var claim = await LoadAsync(claimId, track: true, ct);
        claim.AssignTo(officerId, _user.UserId, Now);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Claim {ClaimNumber} assigned to {OfficerId} by {UserId}", claim.ClaimNumber, officerId, _user.UserId);
        return await MapDetailAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> RequestInformationAsync(Guid claimId, RequestInformationRequest r, CancellationToken ct)
    {
        var claim = await LoadForOfficerAsync(claimId, ct);
        claim.RequestInformation(_user.UserId, r.Message, r.DueDate is null ? null : ToUtc(r.DueDate.Value), Now);
        await _db.SaveChangesAsync(ct);
        return await MapDetailAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> AssessAsync(Guid claimId, AssessClaimRequest r, CancellationToken ct)
    {
        var claim = await LoadForOfficerAsync(claimId, ct);
        claim.RecordAssessment(_user.UserId, r.EstimatedLoss, r.RecommendedPayout, r.Notes, Now);
        await _db.SaveChangesAsync(ct);
        return await MapDetailAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> ApproveAsync(Guid claimId, ApproveClaimRequest r, CancellationToken ct)
    {
        var claim = await LoadForOfficerAsync(claimId, ct);
        claim.Approve(_user.UserId, r.ApprovedAmount, r.Notes, claim.Policy.CoverageLimit, Now);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Claim {ClaimNumber} approved for {Amount} {Currency}", claim.ClaimNumber, r.ApprovedAmount, claim.Currency);
        return await MapDetailAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> RejectAsync(Guid claimId, RejectClaimRequest r, CancellationToken ct)
    {
        var claim = await LoadForOfficerAsync(claimId, ct);
        claim.Reject(_user.UserId, r.Reason, Now);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Claim {ClaimNumber} rejected", claim.ClaimNumber);
        return await MapDetailAsync(claim, ct);
    }

    // ------------------------------------------------------------------ documents

    public async Task<DocumentDto> UploadDocumentAsync(Guid claimId, string fileName, Stream content, CancellationToken ct)
    {
        if (_user.Role == UserRole.Manager) throw new ForbiddenException();
        var claim = await LoadAsync(claimId, track: true, ct);
        if (_user.Role == UserRole.ClaimsOfficer && claim.AssignedOfficerId != _user.UserId)
            throw new ForbiddenException("This claim is not assigned to you.");

        var safeName = Path.GetFileName(fileName ?? string.Empty);
        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 200)
            throw FileError("File name is missing or too long.");
        if (!ClaimsPolicy.AllowedExtensions.Contains(ext))
            throw FileError($"File type '{ext}' is not allowed. Allowed: {string.Join(", ", ClaimsPolicy.AllowedExtensions)}.");

        // Buffer (bounded) so we can verify size and magic bytes before anything touches storage.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > ClaimsPolicy.MaxUploadBytes)
                throw FileError($"File exceeds the {ClaimsPolicy.MaxUploadBytes / (1024 * 1024)} MB limit.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) throw FileError("File is empty.");
        if (!SignatureMatches(ext, buffer.GetBuffer().AsSpan(0, (int)Math.Min(8, buffer.Length))))
            throw FileError("File content does not match its extension.");

        buffer.Position = 0;
        var documentId = Guid.NewGuid();
        var key = $"{claim.Id:N}/{documentId:N}{ext}";
        await _storage.SaveAsync(key, buffer, ct);

        var now = Now;
        var document = new ClaimDocument(documentId, claim.Id, safeName, key, ContentTypes[ext], buffer.Length, _user.UserId, now);
        try
        {
            claim.AddDocument(document, _user.UserId, now);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            await _storage.DeleteAsync(key, CancellationToken.None); // do not leave orphaned files behind
            throw;
        }

        var uploader = await _db.Users.AsNoTracking().Where(u => u.Id == _user.UserId).Select(u => u.FullName).FirstAsync(ct);
        return new DocumentDto(document.Id, document.FileName, document.ContentType, document.SizeBytes, uploader, document.UploadedAt);
    }

    public async Task<DocumentDownload> DownloadDocumentAsync(Guid claimId, Guid documentId, CancellationToken ct)
    {
        var claim = await LoadAsync(claimId, track: false, ct); // enforces claimant ownership
        var doc = claim.Documents.FirstOrDefault(d => d.Id == documentId) ?? throw new NotFoundException("Document", documentId);
        var stream = await _storage.OpenReadAsync(doc.StorageKey, ct) ?? throw new NotFoundException("Document file", documentId);
        return new DocumentDownload(stream, doc.FileName, doc.ContentType);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Claim> LoadForOfficerAsync(Guid claimId, CancellationToken ct)
    {
        if (_user.Role != UserRole.ClaimsOfficer) throw new ForbiddenException("Only claims officers can perform this action.");
        var claim = await LoadAsync(claimId, track: true, ct);
        if (claim.AssignedOfficerId != _user.UserId) throw new ForbiddenException("This claim is not assigned to you.");
        return claim;
    }

    private async Task<Claim> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        IQueryable<Claim> q = _db.Claims
            .Include(c => c.Policy).Include(c => c.Claimant)
            .Include(c => c.Documents).Include(c => c.Assessments).Include(c => c.Assignments)
            .Include(c => c.InformationRequests).Include(c => c.Events).Include(c => c.Decision);
        if (!track) q = q.AsNoTracking();

        var claim = await q.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Claim", id);
        // 404 rather than 403 so claim ids cannot be probed.
        if (IsClaimant && claim.ClaimantId != _user.ClaimantId) throw new NotFoundException("Claim", id);
        return claim;
    }

    private async Task<ClaimDetailDto> MapDetailAsync(Claim c, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        if (c.AssignedOfficerId is { } assigned) ids.Add(assigned);
        foreach (var e in c.Events) if (e.ActorUserId is { } a) ids.Add(a);
        foreach (var a in c.Assignments) { ids.Add(a.OfficerId); ids.Add(a.AssignedByUserId); }
        foreach (var d in c.Documents) ids.Add(d.UploadedByUserId);
        foreach (var s in c.Assessments) ids.Add(s.OfficerId);
        foreach (var i in c.InformationRequests) ids.Add(i.RequestedByUserId);
        if (c.Decision is not null) ids.Add(c.Decision.DecidedByUserId);

        var idList = ids.ToList();
        var names = await _db.Users.AsNoTracking().Where(u => idList.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        string Name(Guid id) => id == AutoAssignmentService.SystemActor ? "System" : names.GetValueOrDefault(id, "Unknown");

        var staff = !IsClaimant;
        var p = c.Policy;

        return new ClaimDetailDto(
            c.Id, c.ClaimNumber, c.Type, c.Market, c.Status, c.Currency, c.ClaimedAmount,
            staff ? c.EstimatedExposure : null,
            c.IncidentDate, c.IncidentLocation, c.Description, c.SubmittedAt, c.UpdatedAt,
            ClaimsPolicy.IsOverdue(c, Now), c.AssignedOfficerId,
            c.AssignedOfficerId is { } oid ? Name(oid) : null,
            new PolicyDto(p.Id, p.PolicyNumber, p.Type, p.Market, p.Currency, p.CoverageLimit, p.StartDate, p.EndDate, p.Description),
            new ClaimantDto(c.Claimant.Id, c.Claimant.FullName, c.Claimant.Email, c.Claimant.Phone, c.Claimant.Market),
            c.Documents.OrderBy(d => d.UploadedAt)
                .Select(d => new DocumentDto(d.Id, d.FileName, d.ContentType, d.SizeBytes, Name(d.UploadedByUserId), d.UploadedAt)).ToList(),
            c.InformationRequests.OrderBy(i => i.RequestedAt)
                .Select(i => new InformationRequestDto(i.Id, i.Message, Name(i.RequestedByUserId), i.RequestedAt, i.DueDate, i.Response, i.RespondedAt, i.IsOpen)).ToList(),
            // Internal assessment notes are never exposed to claimants.
            staff
                ? c.Assessments.OrderBy(s => s.AssessedAt)
                    .Select(s => new AssessmentDto(s.Id, Name(s.OfficerId), s.EstimatedLoss, s.RecommendedPayout, s.Notes, s.AssessedAt)).ToList()
                : new List<AssessmentDto>(),
            staff
                ? c.Assignments.OrderBy(a => a.AssignedAt)
                    .Select(a => new AssignmentDto(a.Id, a.OfficerId, Name(a.OfficerId), Name(a.AssignedByUserId), a.AssignedAt, a.UnassignedAt)).ToList()
                : new List<AssignmentDto>(),
            c.Decision is null ? null
                : new DecisionDto(c.Decision.Outcome, c.Decision.ApprovedAmount, c.Currency, c.Decision.Reason, Name(c.Decision.DecidedByUserId), c.Decision.DecidedAt),
            c.Events.Where(e => staff || e.Type != ClaimEventType.AssessmentRecorded).OrderBy(e => e.OccurredAt)
                .Select(e => new ClaimEventDto(e.Type, e.ActorUserId is { } actor ? Name(actor) : "System", e.Description, e.OccurredAt)).ToList());
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static ValidationException FileError(string message) =>
        new(new[] { new ValidationFailure("file", message) });

    private static bool SignatureMatches(string ext, ReadOnlySpan<byte> head) => ext switch
    {
        ".pdf" => head.StartsWith("%PDF"u8),
        ".png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        ".jpg" or ".jpeg" => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".docx" => head.StartsWith("PK"u8),
        _ => false
    };
}
