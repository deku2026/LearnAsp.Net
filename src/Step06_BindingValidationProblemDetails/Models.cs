using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace Step06_BindingValidationProblemDetails;

public sealed class CreateCourseBody
{
    [Required]
    [MinLength(2)]
    [MaxLength(16)]
    public string Code { get; set; } = "";

    [Required]
    [MinLength(2)]
    public string Title { get; set; } = "";

    [Range(1, 10)]
    public int Credits { get; set; }
}

public sealed class CreateSectionBody : IValidatableObject
{
    [Required]
    public Guid CourseId { get; set; }

    [Required]
    [ValidTermCode]
    public string Term { get; set; } = "";

    [Range(1, 500)]
    public int Capacity { get; set; }

    // IValidatableObject: cross-field validation (can access DI via ValidationContext.GetService)
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Term.StartsWith("INTENSIVE", StringComparison.OrdinalIgnoreCase) && Capacity % 2 != 0)
        {
            yield return new ValidationResult(
                "INTENSIVE terms require even Capacity.",
                [nameof(Term), nameof(Capacity)]);
        }
    }
}

/// <summary>Custom ValidationAttribute: term must match YYYY + S/F + digit (e.g. 2026S1).</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ValidTermCodeAttribute : ValidationAttribute
{
    private static readonly System.Text.RegularExpressions.Regex Pattern =
        new(@"^\d{4}[SFsf]\d$", System.Text.RegularExpressions.RegexOptions.Compiled);

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is string s && Pattern.IsMatch(s))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            $"{context.DisplayName} must match format YYYY + S/F + digit (e.g. 2026S1).",
            [context.MemberName ?? "Term"]);
    }
}

/// <summary>Cross-field FluentValidation (kept for comparison with IValidatableObject).</summary>
public sealed class CreateSectionBodyValidator : AbstractValidator<CreateSectionBody>
{
    public CreateSectionBodyValidator()
    {
        RuleFor(x => x.Term).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Capacity).GreaterThan(0);
        RuleFor(x => x)
            .Must(x => !x.Term.StartsWith("INTENSIVE", StringComparison.OrdinalIgnoreCase) || x.Capacity % 2 == 0)
            .WithMessage("INTENSIVE terms require even Capacity.")
            .WithErrorCode("section.intensive_even_capacity");
    }
}
