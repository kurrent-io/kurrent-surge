namespace Kurrent.Testkit.TUnit;

/// <summary>
/// Assembly-level attribute that skips all tests in a specific class or namespace.
/// Apply multiple instances to a single file to create a centralized skip registry.
/// </summary>
/// <example>
/// <code>
/// // Skip a single test class
/// [assembly: SkipAll(typeof(StreamReadTests), "Blocked by DEV-1234")]
///
/// // Skip all test classes under a namespace prefix
/// [assembly: SkipAll("Kurrent.Surge.Tests.Integration.Streams", "Server upgrade in progress")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class SkipAllAttribute : SkipAttribute {
    readonly Type? _targetClass;
    readonly string? _namespacePrefix;

    /// <summary>
    /// Skips all tests in the specified test class.
    /// </summary>
    /// <param name="targetClass">The test class whose tests should be skipped.</param>
    /// <param name="reason">Reason displayed in the test report for each skipped test.</param>
    public SkipAllAttribute(Type targetClass, string reason) : base(reason) =>
        _targetClass = targetClass;

    /// <summary>
    /// Skips all tests in classes whose namespace starts with <paramref name="namespacePrefix"/>.
    /// </summary>
    /// <param name="namespacePrefix">
    /// The namespace prefix to match. Uses ordinal starts-with comparison,
    /// so <c>"Foo.Bar"</c> matches <c>Foo.Bar</c>, <c>Foo.Bar.Baz</c>, etc.
    /// </param>
    /// <param name="reason">Reason displayed in the test report for each skipped test.</param>
    public SkipAllAttribute(string namespacePrefix, string reason) : base(reason) =>
        _namespacePrefix = namespacePrefix;

    /// <inheritdoc />
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(
            _targetClass is not null
                ? context.TestDetails.ClassType == _targetClass
                : context.TestDetails.ClassType.Namespace?.StartsWith(_namespacePrefix!, StringComparison.Ordinal) is true
        );
}
