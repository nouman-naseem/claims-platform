using Claims.Domain.Common;
using Claims.Domain.Enums;

namespace Claims.Domain.Entities;

public class User : Entity
{
    private User() { }

    public User(string email, string passwordHash, string fullName, UserRole role, Market market, DateTime now)
    {
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        FullName = fullName.Trim();
        Role = role;
        Market = market;
        IsActive = true;
        CreatedAt = now;
    }

    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public Market Market { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
}

public class Claimant : Entity
{
    private Claimant() { }

    public Claimant(Guid userId, string fullName, string email, string phone, Market market)
    {
        UserId = userId;
        FullName = fullName.Trim();
        Email = email.Trim().ToLowerInvariant();
        Phone = phone.Trim();
        Market = market;
    }

    public Guid UserId { get; private set; }
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string Phone { get; private set; } = default!;
    public Market Market { get; private set; }
}

/// <summary>
/// Policies are issued by an upstream system. They exist before the claimant registers and are
/// linked to a claimant by matching <see cref="HolderEmail"/> on registration.
/// </summary>
public class Policy : Entity
{
    private Policy() { }

    public Policy(string policyNumber, string holderEmail, ClaimType type, Market market, decimal coverageLimit,
        DateTime startDate, DateTime endDate, string description, Guid? claimantId = null)
    {
        PolicyNumber = policyNumber;
        HolderEmail = holderEmail.Trim().ToLowerInvariant();
        Type = type;
        Market = market;
        CoverageLimit = coverageLimit;
        StartDate = startDate.Date;
        EndDate = endDate.Date;
        Description = description;
        ClaimantId = claimantId;
    }

    public string PolicyNumber { get; private set; } = default!;
    public string HolderEmail { get; private set; } = default!;
    public Guid? ClaimantId { get; private set; }
    public ClaimType Type { get; private set; }
    public Market Market { get; private set; }
    public decimal CoverageLimit { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    /// <summary>Insured vehicle or property address.</summary>
    public string Description { get; private set; } = default!;

    public string Currency => Market.Currency();

    public bool IsActiveOn(DateTime date) => date.Date >= StartDate && date.Date <= EndDate;

    public void LinkTo(Guid claimantId)
    {
        if (ClaimantId is not null) throw new DomainException("Policy is already linked to a claimant.");
        ClaimantId = claimantId;
    }
}
