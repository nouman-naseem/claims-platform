using Claims.Domain.Enums;

namespace Claims.Application.Contracts;

public record LoginRequest(string Email, string Password);

public record RegisterClaimantRequest(string Email, string Password, string FullName, string Phone, Market Market);

public record UserDto(Guid Id, string Email, string FullName, UserRole Role, Market Market);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

public record PolicyDto(Guid Id, string PolicyNumber, ClaimType Type, Market Market, string Currency,
    decimal CoverageLimit, DateTime StartDate, DateTime EndDate, string Description);

public record OfficerDto(Guid Id, string FullName, Market Market, int OpenClaims);
