using FluentValidation;

namespace Telemetry.Api.Api;

public class TelemetryBatchValidator : AbstractValidator<TelemetryIngestBatch>
{
    public TelemetryBatchValidator()
    {
        RuleFor(x => x.Events).NotEmpty().WithMessage("At least one event is required.");
        RuleForEach(x => x.Events).ChildRules(evt =>
        {
            evt.RuleFor(e => e.Timestamp)
                .NotEmpty()
                .Must(ts => ts >= DateTime.UtcNow.AddDays(-7) && ts <= DateTime.UtcNow.AddHours(1))
                .WithMessage("Timestamp must be within the last 7 days and no more than 1 hour in the future.");

            evt.RuleFor(e => e.Source)
                .NotEmpty()
                .MaximumLength(100)
                .Matches(@"^[\w\-\.]+$")
                .WithMessage("Source must contain only alphanumeric characters, hyphens, underscores, or dots.");

            evt.RuleFor(e => e.MetricName)
                .NotEmpty()
                .MaximumLength(100)
                .Matches(@"^[\w\-\.]+$")
                .WithMessage("MetricName must contain only alphanumeric characters, hyphens, underscores, or dots.");

            evt.RuleFor(e => e.MetricValue).NotNull();
        });
    }
}
