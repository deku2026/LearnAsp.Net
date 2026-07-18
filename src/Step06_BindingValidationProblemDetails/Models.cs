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

public sealed class CreateSectionBody
{
    [Required]
    public Guid CourseId { get; set; }

    [Required]
    public string Term { get; set; } = "";

    [Range(1, 500)]
    public int Capacity { get; set; }
}

/// <summary>Cross-field: Capacity must be even when Term starts with "INTENSIVE".</summary>
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
