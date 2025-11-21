using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
public class InspectRecordTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }


    [Test]
    [Skip("Needs investigation")]
    public async Task inspects_log_record(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var firstMessage = simulation.Records.First();

        var linkedRecord = await Fixture.Client.Streams
            .ReadFirstStreamRecord("$ce-TicTacToe", ct)
            .ShouldNotThrowOrFailAsync(record => { record.IndexRevision.ShouldBeGreaterThan(0); });

        await Fixture.Client.Streams
            .InspectRecord(simulation.Position, ct)
            .ShouldNotThrowOrFailAsync(record => { record.IsDecoded.ShouldBeFalse(); });
    }

    [Test]
    public async ValueTask inspects_record_at_specific_position(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, ct)
            .ShouldNotThrowOrFailAsync();

        record.Position.ShouldBeLessThanOrEqualTo(seeded.Position);
        record.Stream.ShouldNotBe(StreamName.None);
        record.IsDecoded.ShouldBeFalse();
    }

    [Test]
    public async ValueTask inspects_first_record_using_earliest_position(CancellationToken ct) {
        await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Earliest, ct)
            .ShouldNotThrowOrFailAsync();

        record.Position.ShouldBeGreaterThanOrEqualTo(0);
        record.Stream.ShouldNotBe(StreamName.None);
        record.IsDecoded.ShouldBeFalse();
    }

    [Test]
    public async ValueTask inspects_last_record_using_latest_position(CancellationToken ct) {
        await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Latest, ct)
            .ShouldNotThrowOrFailAsync();

        record.Position.ShouldBeGreaterThan(0);
        record.Stream.ShouldNotBe(StreamName.None);
        record.IsDecoded.ShouldBeFalse();
    }

    [Test]
    public async ValueTask inspects_record_with_decoding_disabled(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(5, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: true, ct)
            .ShouldNotThrowOrFailAsync();

        record.IsDecoded.ShouldBeFalse();
        record.Data.Length.ShouldBeGreaterThan(0);
    }

    [Test]
    public async ValueTask inspects_record_with_decoding_enabled(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(5, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: false, ct)
            .ShouldNotThrowOrFailAsync();

        record.IsDecoded.ShouldBeTrue();
        record.Value.ShouldNotBeNull();
    }

    [Test]
    public async ValueTask fails_when_position_does_not_exist(CancellationToken ct) {
        var nonExistentPosition = int.MaxValue - 1000;

        await Fixture.Client.Streams
            .InspectRecord(nonExistentPosition, ct)
            // .ShouldFailAsync(error => error.Value.ShouldBeOfType<LogPositionNotFoundException>());
            .ShouldFailAsync(error => error.Case.ShouldBe(InspectRecordError.InspectRecordErrorCase.LogPositionNotFoundException));;
    }

    [Test]
    public async ValueTask throws_when_position_is_unset() {
        await Fixture.Client.Streams
            .InspectRecord(LogPosition.Unset, CancellationToken.None)
            .ShouldFailAsync(error => {
                error.IsException.ShouldBeTrue();
                var exception = error.AsException;
                exception.ShouldBeOfType<ArgumentOutOfRangeException>();
                ((ArgumentOutOfRangeException)exception).ParamName.ShouldBe("position");
                exception.Message.ShouldContain("Log position cannot be unset");
            });
    }

    [Test]
    public async ValueTask inspected_record_contains_expected_metadata(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(3, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: false, ct)
            .ShouldNotThrowOrFailAsync();

        record.Id.ShouldNotBe(Guid.Empty);
        record.Position.ShouldBeGreaterThanOrEqualTo(0);
        record.StreamRevision.ShouldBeGreaterThanOrEqualTo(0);
        record.Timestamp.ShouldNotBe(default);
        record.Schema.SchemaName.ShouldNotBe(SchemaName.None);
    }

    [Test]
    public async ValueTask inspects_record_from_middle_of_stream(CancellationToken ct) {
        // Seed multiple activities to create a log with multiple records
        await Fixture.SeedSmartHomeActivity(5, ct);
        var seeded = await Fixture.SeedSmartHomeActivity(5, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, ct)
            .ShouldNotThrowOrFailAsync();

        record.Position.ShouldBeLessThanOrEqualTo(seeded.Position);
        record.IsDecoded.ShouldBeFalse();
    }
}
