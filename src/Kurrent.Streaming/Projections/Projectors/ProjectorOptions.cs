using Kurrent.Streaming.Options;
using Kurrent.Validation;

namespace Kurrent.Projections;

/// <summary>
/// Options for running a projection via <see cref="KurrentProjector"/>. Inherits all configuration
/// from <see cref="ProcessorOptions"/> (connection, subscription, checkpoint) and adds a
/// <see cref="ResolveClient"/> delegate for projection-specific client resolution.
/// </summary>
[PublicAPI]
public record ProjectorOptions<TClient> : ProcessorOptions {
    /// <summary>
    /// Resolves the client used by the projection. Invoked during <c>OnConnected</c> (for <c>Setup</c>)
    /// and on each record dispatch that uses a with-client handler overload. User-supplied —
    /// caching, pooling, or fresh-per-call semantics are the responsibility of the delegate.
    /// </summary>
    public GetProjectionClient<TClient>? ResolveClient { get; set; }

    protected override ValidationResult RunValidation() {
        var baseResult = base.RunValidation();
        var ownResult  = ProjectorValidator.Instance.Validate(this);

        if (baseResult.IsValid && ownResult.IsValid)
            return ValidationResult.Valid;

        var problems = baseResult.Problems.Concat(ownResult.Problems).ToArray();
        var code     = baseResult.IsInvalid ? baseResult.ErrorCode : ownResult.ErrorCode;
        return ValidationResult.Failure(code, problems);
    }

    public sealed class ProjectorValidator : Validator<ProjectorValidator, ProjectorOptions<TClient>> {
        public override string ErrorCode => "INVALID_PROJECTOR_OPTIONS";

        public ProjectorValidator() {
            RuleFor(o => o.ResolveClient)
                .Must(resolver => resolver is not null,
                    "ResolveClient is required. Set ProjectorOptions.ResolveClient before running the projection.");
        }
    }
}
