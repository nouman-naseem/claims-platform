namespace Claims.Application.Security;

/// <summary>Role names as they appear in the JWT "role" claim (match the UserRole enum names).</summary>
public static class Roles
{
    public const string Claimant = "Claimant";
    public const string ClaimsOfficer = "ClaimsOfficer";
    public const string Manager = "Manager";
    public const string Staff = ClaimsOfficer + "," + Manager;
    public const string ClaimantOrOfficer = Claimant + "," + ClaimsOfficer;
}
