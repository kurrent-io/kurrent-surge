using Bogus;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Schema.Serialization;

/// <summary>
/// Shared test fixture for all <see cref="ISchemaSerializer"/> implementations.
/// Tests serialization, deserialization, and round-trip correctness across
/// multiple message types per serializer format.
/// </summary>
public abstract class SchemaSerializerTestFixture {
    protected static Faker Faker => new Faker();

    protected ISchemaSerializer Sut = null!;

    [Before(Test)]
    public void SetUp() {
        Sut = CreateSut();
        OnSetUp();
    }

    [After(Test)]
    public void TearDown() => OnTearDown();

    protected abstract ISchemaSerializer CreateSut();

    protected abstract SchemaDataFormat ExpectedDataFormat { get; }

    protected abstract object[] CreateTestMessages();

    protected abstract SchemaSerializationContext CreateSerializeContext();

    protected virtual SchemaSerializationContext CreateDeserializeContext(
        SchemaSerializationContext afterSerialize
    ) =>
        new() {
            Stream       = afterSerialize.Stream,
            RecordSchema = afterSerialize.RecordSchema
        };

    protected virtual async ValueTask AssertDeserialized(object original, object? deserialized) {
        await Assert.That(deserialized).IsNotNull();
        await Assert.That(deserialized).IsEqualTo(original);
    }

    protected virtual void OnSetUp() { }

    protected virtual void OnTearDown() { }

    #region . Tests .

    [Test]
    public async ValueTask data_format_matches_expected() {
        // Assert
        await Assert.That(Sut.DataFormat).IsEqualTo(ExpectedDataFormat);
    }

    [Test]
    public async ValueTask serialize_null_returns_empty_bytes() {
        // Arrange
        var context = CreateSerializeContext();

        // Act
        var result = await Sut.Serialize(null, context).ConfigureAwait(false);

        // Assert
        await Assert.That(result.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask serialize_produces_non_empty_bytes() {
        foreach (var message in CreateTestMessages()) {
            // Arrange
            var context = CreateSerializeContext();

            // Act
            var result = await Sut.Serialize(message, context).ConfigureAwait(false);

            // Assert
            await Assert.That(result.Length).IsGreaterThan(0);
        }
    }

    [Test]
    public async ValueTask serialize_and_deserialize_round_trips() {
        foreach (var message in CreateTestMessages()) {
            // Arrange
            var context = CreateSerializeContext();
            var bytes   = await Sut.Serialize(message, context).ConfigureAwait(false);

            var deserializeContext = CreateDeserializeContext(context);

            // Act
            var result = await Sut.Deserialize(bytes, deserializeContext).ConfigureAwait(false);

            // Assert
            await Assert.That(result).IsTypeOf<DeserializationResult.Deserialized>();
            await AssertDeserialized(message, ((DeserializationResult.Deserialized)result).Value);
        }
    }

    #endregion
}
