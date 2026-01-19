using FluentValidation;

namespace Mapsicle.Examples;

/// <summary>
/// Validator for ValidatedUserDto - demonstrates FluentValidation rules.
/// </summary>
public class ValidatedUserDtoValidator : AbstractValidator<ValidatedUserDto>
{
    public ValidatedUserDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("A valid email address is required");

        RuleFor(x => x.Age)
            .GreaterThan(0).WithMessage("Age must be positive")
            .LessThanOrEqualTo(150).WithMessage("Age must be realistic");
    }
}

/// <summary>
/// Stricter validator for adult users only.
/// </summary>
public class AdultUserValidator : AbstractValidator<ValidatedUserDto>
{
    public AdultUserValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required");

        RuleFor(x => x.Email)
            .NotEmpty().EmailAddress();

        RuleFor(x => x.Age)
            .GreaterThanOrEqualTo(18).WithMessage("User must be 18 or older")
            .LessThanOrEqualTo(120).WithMessage("Please enter a valid age");
    }
}
