using Bogus;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams;
using Kurrent.Testing.Bogus;

namespace Kurrent.Client.Tests.Schema.Serialization;

/// <summary>
/// Shared test fixture for all <see cref="ISchemaSerializer"/> implementations.
/// Tests serialization, deserialization, and round-trip correctness across
/// multiple message types per serializer format.
/// </summary>
public abstract class SchemaSerializerTestFixture {
    protected static Faker Faker => BogusFaker.Shared;

    protected ISchemaSerializer _sut = null!;

    [Before(Test)]
    public void SetUp() {
        _sut = CreateSut();
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
        await Assert.That(_sut.DataFormat).IsEqualTo(ExpectedDataFormat);
    }

    [Test]
    public async ValueTask serialize_null_returns_empty_bytes() {
        var context = CreateSerializeContext();

        var result = await _sut.Serialize(null, context).ConfigureAwait(false);

        await Assert.That(result.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask serialize_produces_non_empty_bytes() {
        foreach (var message in CreateTestMessages()) {
            var context = CreateSerializeContext();

            var result = await _sut.Serialize(message, context).ConfigureAwait(false);

            await Assert.That(result.Length).IsGreaterThan(0);
        }
    }

    [Test]
    public async ValueTask serialize_and_deserialize_round_trips() {
        foreach (var message in CreateTestMessages()) {
            var context = CreateSerializeContext();

            var bytes = await _sut.Serialize(message, context).ConfigureAwait(false);

            var deserializeContext = CreateDeserializeContext(context);
            var result             = await _sut.Deserialize(bytes, deserializeContext).ConfigureAwait(false);

            await AssertDeserialized(message, result);
        }
    }

    #endregion
}
