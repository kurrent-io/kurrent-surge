namespace Kurrent.Surge.Tests.Errors;

/// <summary>
/// Pins <see cref="KurrentErrorCodes"/> to the protocol enums. The constants live in Kurrent.Surge.Core,
/// which cannot see the compiled protos; this is the only place both are visible, so this is where the
/// vocabulary is held to the wire. A code added or renamed in a proto fails here.
/// </summary>
[Category("Errors")]
public class KurrentErrorCodesTests {
    #region ->> Test Case Generators <<-

    public record ErrorCodeModule(IReadOnlyList<ErrorCode> Declared, IReadOnlyList<ErrorCode> Protocol);

    public class ProtocolModules : TestCaseDataGenerator<ErrorCodeModule> {
        protected override IEnumerable<TestCase<ErrorCodeModule>> Data() {
            yield return new(new([.. KurrentErrorCodes.Streams.All],  ProtocolErrorCodes.Streams),  DisplayName: "streams");
            yield return new(new([.. KurrentErrorCodes.Registry.All], ProtocolErrorCodes.Registry), DisplayName: "registry");
            yield return new(new([.. KurrentErrorCodes.Indexes.All],  ProtocolErrorCodes.Indexes),  DisplayName: "indexes");
            yield return new(new([.. KurrentErrorCodes.Server.All],   ProtocolErrorCodes.Server),   DisplayName: "server");
        }
    }

    #endregion

    [Test]
    [ProtocolModules]
    public async ValueTask error_codes_match_the_protocol_enum(ErrorCodeModule module) {
        // Arrange
        var expected = module.Protocol.Select(x => x.Value).Order().ToList();

        // Act
        var actual = module.Declared.Select(x => x.Value).Order().ToList();

        // Assert
        await Assert.That(actual).IsEquivalentTo(expected);
    }
}
