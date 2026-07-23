using System.Reflection;
using Kurrent.Surge.Client.Exceptions;

using IndexesErrors = KurrentDB.Protocol.V2.Indexes.Errors;
using RegistryErrors = KurrentDB.Protocol.V2.Registry.Errors;
using ServerErrors = Kurrent.Rpc;
using StreamsErrors = KurrentDB.Protocol.V2.Streams.Errors;

namespace Kurrent.Surge.Tests.Errors;

/// <summary>
/// Pins the exception vocabulary to the protocol enums. Each exception owns its <c>Code</c>; this is the
/// only place the compiled protos and those codes are both visible, so this is where they are held to the
/// wire. The completeness guarantee the old generator never gave us: a code added or renamed in a proto
/// fails here, and so does an exception whose code drifts from the enum.
/// </summary>
[Category("Errors")]
public class ErrorCodeCoverageTests {
    #region ->> Test Case Generators <<-

    public record ErrorModule(string Name, IReadOnlyList<ErrorCode> Protocol, IReadOnlyList<ErrorCode> KnownGaps);

    public class ProtocolModules : TestCaseDataGenerator<ErrorModule> {
        protected override IEnumerable<TestCase<ErrorModule>> Data() {
            // The server has no streams read RPC yet, so it never emits these two — no exception until it does.
            yield return new(new(
                "streams",
                ProtocolCodes<StreamsErrors.StreamsError>(),
                [ErrorCode.From("RECORD_NOT_FOUND"), ErrorCode.From("STREAM_REVISION_NOT_FOUND")]),
                DisplayName: "streams");

            yield return new(new("registry", ProtocolCodes<RegistryErrors.RegistryError>(), []), DisplayName: "registry");

            yield return new(new("indexes", ProtocolCodes<IndexesErrors.IndexesError>(), []), DisplayName: "indexes");

            yield return new(new("server", ProtocolCodes<ServerErrors.ServerError>(), []), DisplayName: "server");
        }
    }

    #endregion

    /// <summary>
    /// The error code each concrete <see cref="KurrentException"/> subclass declares, discovered by
    /// reflection so a newly added exception is covered automatically. Reflection is fine here — the AOT
    /// ban is on shipping code, not tests.
    /// </summary>
    static readonly IReadOnlySet<string> DeclaredCodes = typeof(KurrentException).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(KurrentException)))
        .Select(t => t.GetProperty("Code", BindingFlags.Public | BindingFlags.Static)?.GetValue(null))
        .OfType<ErrorCode>()
        .Select(x => x.Value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The error-code vocabulary a protocol enum defines. <c>Unspecified</c> is the proto3 zero value and
    /// never travels as an error, so it carries no code.
    /// </summary>
    static IReadOnlyList<ErrorCode> ProtocolCodes<T>() where T : struct, Enum => [
        .. Enum.GetNames<T>()
            .Where(name => name != "Unspecified")
            .Select(ErrorCode.From)
    ];

    [Test]
    [ProtocolModules]
    public async ValueTask every_protocol_code_has_an_exception_except_the_known_gaps(ErrorModule module) {
        // Arrange
        var expectedGaps = module.KnownGaps.Select(x => x.Value).Order().ToList();

        // Act
        var uncovered = module.Protocol
            .Where(code => !DeclaredCodes.Contains(code.Value))
            .Select(x => x.Value)
            .Order()
            .ToList();

        // Assert
        await Assert.That(uncovered).IsEquivalentTo(expectedGaps);
    }
}
