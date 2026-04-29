using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Sdk.Testkit.Fixtures;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AutoRegisterTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.None)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    static readonly ISchemaNameStrategy NameStrategy = SchemaNameStrategies.TypeName();

    [Test]
    public async ValueTask registers_new_schema(CancellationToken ct) {
        // Arrange
        await using var client = KurrentClientOptions.Parse(Fixture.ConnectionString)
            .GetBuilder()
            .WithLoggerFactory(Fixture.LoggerFactory)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .ConfigureSchema(new KurrentClientSchemaOptions {
                Json = new() { SchemaRegistration = SchemaRegistrationOptions.AutoRegister }
            })
            .CreateClient();

        var data = await Fixture.TestData.GenerateRecord(ct);

        var request = AppendStreamRequest.For(data.Stream)
            .WithRecords(data.Records)
            .Build();

        // Act
        await client.Streams.AppendStream(request, ct);

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

        // Assert
        var schemaName = NameStrategy.GenerateSchemaName(data.Records[0].Value!.GetType());
        var schema     = await Fixture.Client.Registry.GetSchema(schemaName, ct);
        var version    = await Fixture.Client.Registry.GetSchemaVersion(schemaName, schema.LatestSchemaVersion, ct);

        await Assert.That(schema.SchemaName).IsEqualTo(schemaName);
        await Assert.That(schema.Details.DataFormat).IsEqualTo(SchemaDataFormat.Json);
        await Assert.That(version.VersionNumber).IsEqualTo(1);
    }

    [Test]
    public async ValueTask caches_first_registration(CancellationToken ct) {
        // Arrange
        await using var client = KurrentClientOptions.Parse(Fixture.ConnectionString)
            .GetBuilder()
            .WithLoggerFactory(Fixture.LoggerFactory)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .ConfigureSchema(new KurrentClientSchemaOptions {
                Json = new() { SchemaRegistration = SchemaRegistrationOptions.AutoRegister }
            })
            .CreateClient();

        var data = await Fixture.TestData.GenerateSet(ct);

        // Two records of the same CLR type so the second append hits the SchemaManager cache.
        var sameType = data.Records
            .Where(r => r.Value is LapCompleted)
            .Take(2)
            .ToArray();

        // Act
        await client.Streams.AppendStream(
            AppendStreamRequest.For(data.Stream).WithRecords([sameType[0]]).Build(), ct);

        await client.Streams.AppendStream(
            AppendStreamRequest.For(data.Stream).WithRecords([sameType[1]]).Build(), ct);

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

        // Assert
        var schemaName = NameStrategy.GenerateSchemaName(typeof(LapCompleted));
        var schema     = await Fixture.Client.Registry.GetSchema(schemaName, ct);

        await Assert.That(schema.LatestSchemaVersion).IsEqualTo(1);
    }

    [Test]
    public async ValueTask throws_when_type_is_unmapped(CancellationToken ct) {
        // Arrange
        await using var client = KurrentClientOptions.Parse(Fixture.ConnectionString)
            .GetBuilder()
            .WithLoggerFactory(Fixture.LoggerFactory)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .ConfigureSchema(new KurrentClientSchemaOptions {
                Json = new() { SchemaRegistration = SchemaRegistrationOptions.ManualMap }
            })
            .CreateClient();

        var data = await Fixture.TestData.GenerateRecord(ct);

        var request = AppendStreamRequest.For(data.Stream)
            .WithRecords(data.Records)
            .Build();

        // Act
        // The append-record mapper wraps the serializer's exception in an ArgumentException
        // ("Failed to map AppendRecord for stream ..."), so the registration error surfaces as the inner.
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await client.Streams.AppendStream(request, ct));

        // Assert
        await Assert.That(ex?.InnerException).IsTypeOf<AutoRegistrationDisabledException>();
    }

    [Test]
    public async ValueTask reuses_existing_schema(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateRecord(ct);
        var schemaName = NameStrategy.GenerateSchemaName(data.Records.First().Value!.GetType());

        try {
            await Fixture.Client.Registry.CreateSchema(
                schemaName,
                KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson(),
                SchemaDataFormat.Json,
                ct);
        }
        catch (SchemaAlreadyExistsException) {
            // schema may already exist from a prior run
        }

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

        await using var client = KurrentClientOptions.Parse(Fixture.ConnectionString)
            .GetBuilder()
            .WithLoggerFactory(Fixture.LoggerFactory)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .ConfigureSchema(new KurrentClientSchemaOptions {
                Json = new() { SchemaRegistration = SchemaRegistrationOptions.AutoRegister }
            })
            .CreateClient();

        var request = AppendStreamRequest.For(data.Stream)
            .WithRecords(data.Records)
            .Build();

        // Act
        await client.Streams.AppendStream(request, ct);

        // Assert
        // Auto-register must reuse the pre-existing schema rather than creating a new version.
        var schema = await Fixture.Client.Registry.GetSchema(schemaName, ct);
        await Assert.That(schema.LatestSchemaVersion).IsEqualTo(1);
    }
}
