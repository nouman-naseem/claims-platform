using System.Security.Claims;
using Claims.Application.Abstractions;
using Claims.Domain.Enums;

namespace Claims.API.Infrastructure;

/// <summary>Reads the authenticated principal from the JWT claims.</summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal _principal;

    public HttpCurrentUser(IHttpContextAccessor accessor) =>
        _principal = accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public Guid UserId => Guid.TryParse(_principal.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException("Not authenticated.");

    public UserRole Role => Enum.TryParse<UserRole>(_principal.FindFirstValue("role"), out var role)
        ? role : throw new UnauthorizedAccessException("Not authenticated.");

    public Market Market => Enum.TryParse<Market>(_principal.FindFirstValue("market"), out var market)
        ? market : Market.Singapore;

    public Guid? ClaimantId => Guid.TryParse(_principal.FindFirstValue("claimant_id"), out var id) ? id : null;
}
