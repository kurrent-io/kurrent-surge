using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Surge.Tests.Errors;

/// <summary>
/// The completeness guarantee the per-type dispatch never gave us: every code the server can send is
/// either mapped to a typed exception or listed here as a known gap. A code added to a proto lands in
/// neither and fails this.
/// </summary>
[Category("Errors")]
public class KurrentRpcErrorMapperTests {
    #region ->> Test Case Generators <<-

    public record MapperCoverage(IReadOnlyList<ErrorCode> Protocol, IReadOnlyList<ErrorCode> ExpectedGap);

    public class ProtocolModules : TestCaseDataGenerator<MapperCoverage> {
        protected override IEnumerable<TestCase<MapperCoverage>> Data() {
            // No exception type models these two yet; the indexes module has none at all, and its own
            // Exceptions.cs is commented out and holds a copy of the registry schema exceptions.
            yield return new(
                new(ProtocolErrorCodes.Streams, [
                    KurrentErrorCodes.Streams.RecordNotFound,
                    KurrentErrorCodes.Streams.StreamRevisionNotFound
                ]),
                DisplayName: "streams");

            yield return new(new(ProtocolErrorCodes.Registry, []), DisplayName: "registry");

            yield return new(
                new(ProtocolErrorCodes.Indexes, [.. KurrentErrorCodes.Indexes.All]),
                DisplayName: "indexes");

            yield return new(new(ProtocolErrorCodes.Server, []), DisplayName: "server");
        }
    }

    #endregion

    [Test]
    [ProtocolModules]
    public async ValueTask maps_every_protocol_error_code_except_the_known_gap(MapperCoverage coverage) {
        // Arrange
        var expected = coverage.ExpectedGap.Select(x => x.Value).Order().ToList();

        // Act
        var unmapped = coverage.Protocol
            .Where(code => !KurrentRpcErrorMapper.Handles(code))
            .Select(x => x.Value)
            .Order()
            .ToList();

        // Assert
        await Assert.That(unmapped).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask maps_every_connectors_error_code() {
        // Arrange
        var expected = KurrentErrorCodes.Connectors.All.Count;

        // Act
        var mapped = KurrentErrorCodes.Connectors.All.Count(KurrentRpcErrorMapper.Handles);

        // Assert
        await Assert.That(mapped).IsEqualTo(expected);
    }
}
