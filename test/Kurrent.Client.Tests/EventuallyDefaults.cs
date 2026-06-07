using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Tests;

/// <summary>
/// Shared transient-error defaults for <c>EventuallyPasses</c> assertions in this test suite.
/// </summary>
public static class EventuallyDefaults {
    public static readonly Type[] TransientErrors = [
        typeof(SchemaNotFoundException),
        typeof(ConnectorNotFoundException),
        typeof(Google.Rpc.Exceptions.NotFoundException)
    ];

    public static EventuallyOptions RetryOnTransients(CancellationToken ct = default) =>
        new EventuallyOptions { CancellationToken = ct }.RetryOn(TransientErrors);
}
