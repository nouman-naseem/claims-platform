using Claims.Application.Contracts;
using FluentValidation;

namespace Claims.Application.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public class RegisterClaimantRequestValidator : AbstractValidator<RegisterClaimantRequest>
{
    public RegisterClaimantRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30).Matches(@"^[0-9+\-\s()]{6,30}$")
            .WithMessage("Phone number format is invalid.");
        RuleFor(x => x.Market).IsInEnum();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class SubmitClaimRequestValidator : AbstractValidator<SubmitClaimRequest>
{
    public SubmitClaimRequestValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
        RuleFor(x => x.IncidentDate).NotEmpty();
        RuleFor(x => x.IncidentLocation).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(10).MaximumLength(4000);
        RuleFor(x => x.ClaimedAmount).GreaterThan(0).LessThanOrEqualTo(100_000_000m);
    }
}

public class AssignClaimRequestValidator : AbstractValidator<AssignClaimRequest>
{
    public AssignClaimRequestValidator() => RuleFor(x => x.OfficerId).NotEqual(Guid.Empty).When(x => x.OfficerId.HasValue);
}

public class RequestInformationRequestValidator : AbstractValidator<RequestInformationRequest>
{
    public RequestInformationRequestValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.DueDate).Must(d => d!.Value.ToUniversalTime() > DateTime.UtcNow)
            .When(x => x.DueDate.HasValue).WithMessage("Due date must be in the future.");
    }
}

public class ProvideInformationRequestValidator : AbstractValidator<ProvideInformationRequest>
{
    public ProvideInformationRequestValidator() => RuleFor(x => x.Response).NotEmpty().MaximumLength(4000);
}

public class AssessClaimRequestValidator : AbstractValidator<AssessClaimRequest>
{
    public AssessClaimRequestValidator()
    {
        RuleFor(x => x.EstimatedLoss).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100_000_000m);
        RuleFor(x => x.RecommendedPayout).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100_000_000m);
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(4000);
    }
}

public class ApproveClaimRequestValidator : AbstractValidator<ApproveClaimRequest>
{
    public ApproveClaimRequestValidator()
    {
        RuleFor(x => x.ApprovedAmount).GreaterThan(0).LessThanOrEqualTo(100_000_000m);
        RuleFor(x => x.Notes).MaximumLength(4000);
    }
}

public class RejectClaimRequestValidator : AbstractValidator<RejectClaimRequest>
{
    public RejectClaimRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(4000);
}

public class ClaimQueryValidator : AbstractValidator<ClaimQuery>
{
    public ClaimQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
