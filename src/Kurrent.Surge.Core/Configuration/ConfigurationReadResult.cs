namespace Kurrent.Surge;

/// <summary>
/// What a configuration read produced: the value, and everything that went wrong getting there.
/// </summary>
/// <remarks>
/// Reading never throws. A setting the reader cannot use is recorded and skipped, so one bad key
/// cannot cost the rest of the file.
/// </remarks>
/// <typeparam name="T">The options type that was populated.</typeparam>
[PublicAPI]
public sealed record ConfigurationReadResult<T>(T Value, IReadOnlyList<ConfigurationProblem> Problems) {
    /// <summary>
    /// True when nothing was reported.
    /// </summary>
    public bool IsClean => Problems.Count == 0;

    /// <summary>
    /// True when at least one setting could not be applied.
    /// </summary>
    public bool HasErrors => Problems.Any(p => p.Severity == ConfigurationProblemSeverity.Error);

    /// <summary>
    /// Throws when any setting could not be applied. Warnings never throw.
    /// </summary>
    /// <exception cref="InvalidOperationException">At least one problem is an error.</exception>
    public T EnsureValid() {
        if (!HasErrors)
            return Value;

        var errors = Problems.Where(p => p.Severity == ConfigurationProblemSeverity.Error);

        throw new InvalidOperationException(
            $"Configuration could not be applied:{Environment.NewLine}" +
            string.Join(Environment.NewLine, errors.Select(p => $"  {p.Path}: {p.Message}")));
    }

    /// <summary>
    /// Throws on errors and writes warnings to <paramref name="logger"/>.
    /// </summary>
    public T EnsureValid(ILogger logger) {
        foreach (var problem in Problems.Where(p => p.Severity == ConfigurationProblemSeverity.Warning))
            logger.LogWarning("Configuration: {Path} — {Message}", problem.Path, problem.Message);

        return EnsureValid();
    }
}
