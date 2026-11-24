using Claims.Application.Contracts;
using Claims.Application.Security;
using Claims.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[ApiController]
[Route("api/claims")]
[Authorize]
public class ClaimsController : ControllerBase
{
    private readonly ClaimService _claims;
    public ClaimsController(ClaimService claims) => _claims = claims;

    /// <summary>List claims (paged, filterable). Claimants only ever see their own claims.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ClaimSummaryDto>>> List([FromQuery] ClaimQuery query, CancellationToken ct) =>
        Ok(await _claims.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await _claims.GetAsync(id, ct));

    /// <summary>Report a new motor/property incident (claimant).</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Claimant)]
    [ProducesResponseType(typeof(ClaimDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ClaimDetailDto>> Submit(SubmitClaimRequest request, CancellationToken ct)
    {
        var claim = await _claims.SubmitAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = claim.Id }, claim);
    }

    /// <summary>Assign a claim. Officers assign to themselves; managers assign to any officer.</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<ClaimDetailDto>> Assign(Guid id, AssignClaimRequest request, CancellationToken ct) =>
        Ok(await _claims.AssignAsync(id, request, ct));

    [HttpPost("{id:guid}/information-requests")]
    [Authorize(Roles = Roles.ClaimsOfficer)]
    public async Task<ActionResult<ClaimDetailDto>> RequestInformation(Guid id, RequestInformationRequest request, CancellationToken ct) =>
        Ok(await _claims.RequestInformationAsync(id, request, ct));

    [HttpPost("{id:guid}/information-requests/{requestId:guid}/response")]
    [Authorize(Roles = Roles.Claimant)]
    public async Task<ActionResult<ClaimDetailDto>> ProvideInformation(Guid id, Guid requestId, ProvideInformationRequest request, CancellationToken ct) =>
        Ok(await _claims.ProvideInformationAsync(id, requestId, request, ct));

    [HttpPost("{id:guid}/assessments")]
    [Authorize(Roles = Roles.ClaimsOfficer)]
    public async Task<ActionResult<ClaimDetailDto>> Assess(Guid id, AssessClaimRequest request, CancellationToken ct) =>
        Ok(await _claims.AssessAsync(id, request, ct));

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = Roles.ClaimsOfficer)]
    public async Task<ActionResult<ClaimDetailDto>> Approve(Guid id, ApproveClaimRequest request, CancellationToken ct) =>
        Ok(await _claims.ApproveAsync(id, request, ct));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = Roles.ClaimsOfficer)]
    public async Task<ActionResult<ClaimDetailDto>> Reject(Guid id, RejectClaimRequest request, CancellationToken ct) =>
        Ok(await _claims.RejectAsync(id, request, ct));

    /// <summary>Upload a supporting document (pdf, jpg, png, docx; max 10 MB).</summary>
    [HttpPost("{id:guid}/documents")]
    [Authorize(Roles = Roles.ClaimantOrOfficer)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<DocumentDto>> Upload(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await _claims.UploadDocumentAsync(id, file.FileName, stream, ct));
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid documentId, CancellationToken ct)
    {
        var doc = await _claims.DownloadDocumentAsync(id, documentId, ct);
        return File(doc.Content, doc.ContentType, doc.FileName);
    }
}
