using FluentValidation;

namespace Telemetry.Api.Api;

public class TelemetryBatchValidator : AbstractValidator<TelemetryIngestBatch>
{
    public TelemetryBatchValidator()
    {
        RuleFor(x => x.Events).NotEmpty().WithMessage("At least one event is required.");
        RuleForEach(x => x.Events).ChildRules(evt =>
        {
            evt.RuleFor(e => e.Timestamp).NotEmpty();
            evt.RuleFor(e => e.Source).NotEmpty().MaximumLength(100);
            evt.RuleFor(e => e.MetricName).NotEmpty().MaximumLength(100);
            evt.RuleFor(e => e.MetricValue).NotNull();
        });
    }
}
