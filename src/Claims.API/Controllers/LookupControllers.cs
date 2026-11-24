using Claims.Application.Contracts;
using Claims.Application.Security;
using Claims.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[ApiController]
[Authorize]
public class PoliciesController : ControllerBase
{
    private readonly LookupService _lookup;
    public PoliciesController(LookupService lookup) => _lookup = lookup;

    /// <summary>Policies the signed-in claimant can claim against.</summary>
    [HttpGet("api/policies/mine")]
    [Authorize(Roles = Roles.Claimant)]
    public async Task<ActionResult<IReadOnlyList<PolicyDto>>> Mine(CancellationToken ct) => Ok(await _lookup.GetMyPoliciesAsync(ct));
}

[ApiController]
[Authorize(Roles = Roles.Staff)]
public class OfficersController : ControllerBase
{
    private readonly LookupService _lookup;
    public OfficersController(LookupService lookup) => _lookup = lookup;

    [HttpGet("api/officers")]
    public async Task<ActionResult<IReadOnlyList<OfficerDto>>> List(CancellationToken ct) => Ok(await _lookup.GetOfficersAsync(ct));
}

[ApiController]
[Authorize(Roles = Roles.Manager)]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboard;
    public DashboardController(DashboardService dashboard) => _dashboard = dashboard;

    /// <summary>Management dashboard: totals, breakdowns, exposure, workload, overdue.</summary>
    [HttpGet("api/dashboard")]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct) => Ok(await _dashboard.GetAsync(ct));
}
