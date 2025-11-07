using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams"), Category("Streams_Append")]
public class AppendTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask appends_to_single_stream(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        await Fixture.Client.Streams
            .AppendSession([simulation],ct)
            .ShouldNotThrowOrFailAsync(success => {
                success.Position.ShouldBeGreaterThan(LogPosition.Earliest);

                var appendStreamSuccess = success[0];

                appendStreamSuccess.Stream.ShouldBe(simulation.Stream);
                appendStreamSuccess.StreamRevision.ShouldBe(simulation.Messages.Count() - 1);
                appendStreamSuccess.Position.ShouldBeGreaterThan(LogPosition.Earliest);

                appendStreamSuccess.Position.ShouldBeEquivalentTo(success.Position);
            });
    }

    [Test]
    public async ValueTask creates_stream(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        await Fixture.Client.Streams
            .AppendStream(simulation, ct)
            .ShouldNotThrowOrFailAsync(success => {
                success.Stream.ShouldBe(simulation.Stream);
                success.StreamRevision.ShouldBe(simulation.Messages.Count() - 1);
                success.Position.ShouldBeGreaterThan(LogPosition.Earliest);
            });
    }

    [Test]
    public async ValueTask appends_to_existing_stream(CancellationToken ct) {
        var smartHomeActivity = await Fixture.SeedSmartHomeActivity(ct);

        // create stream
        var result = await Fixture.Client.Streams
            .AppendStream(smartHomeActivity.Stream, ExpectedStreamState.NoStream, smartHomeActivity.Records.Take(3), ct)
            .ShouldNotThrowOrFailAsync();

        // append to existing stream
        await Fixture.Client.Streams
            .AppendStream(smartHomeActivity.Stream, result.StreamRevision, smartHomeActivity.Records.Skip(3), ct)
            .ShouldNotThrowOrFailAsync(success => {
                success.Stream.ShouldBe(smartHomeActivity.Stream);
                success.StreamRevision.ShouldBe(smartHomeActivity.Records.Count() - 1);
                success.Position.ShouldBeGreaterThan(LogPosition.Earliest);
            });
    }

    public class AppendToStreamInAnyState : TestCaseGenerator<ExpectedStreamState> {
        protected override IEnumerable<ExpectedStreamState> Data() => [
            ExpectedStreamState.NoStream,
            ExpectedStreamState.StreamExists
        ];
    }

    [Test, AppendToStreamInAnyState]
    public async Task appends_to_stream_in_any_state(ExpectedStreamState expectedStreamState, CancellationToken ct) {
        var smartHomeActivity = await Fixture.SeedSmartHomeActivity(ct);

        // var simulatedGame = Fixture.TrySimulateGame(GamesAvailable.TicTacToe);

        if (ExpectedStreamState.NoStream == expectedStreamState) {
            var expectedRevision = StreamRevision.From(smartHomeActivity.Records.Count() - 1);

            // should create the stream
            await Fixture.Client.Streams
                .AppendStream(smartHomeActivity.Stream, ExpectedStreamState.Any, smartHomeActivity.Records, ct)
                .ShouldNotThrowOrFailAsync(success => {
                    success.Stream.ShouldBe(smartHomeActivity.Stream);
                    success.StreamRevision.ShouldBe(expectedRevision);
                    success.Position.ShouldBeGreaterThan(LogPosition.Earliest);
                });
        }
        else if(ExpectedStreamState.StreamExists == expectedStreamState) {
            // create stream
            await Fixture.Client.Streams
                .AppendStream(smartHomeActivity.Stream, ExpectedStreamState.NoStream, smartHomeActivity.Records.Take(3), ct)
                .ShouldNotThrowOrFailAsync();

            // append to existing stream
            await Fixture.Client.Streams
                .AppendStream(smartHomeActivity.Stream, ExpectedStreamState.StreamExists, smartHomeActivity.Records.Skip(3), ct)
                .ShouldNotThrowOrFailAsync();
        }
    }


    public class CreatesMultipleStreamsTransactionallyState : TestCaseGenerator<int> {
        protected override IEnumerable<int> Data() => [
            2, 5, 6, 8, 10, 15, 20, 25, 30, 50, 100
        ];
    }

    [Test, CreatesMultipleStreamsTransactionallyState]
    public async Task creates_multiple_streams_transactionally(int streams, CancellationToken ct) {
        var activity = HomeAutomationTestData.SimulateHousingComplexActivity();

        var requests = activity
            .Select(act => act.AppendRequest)
            .ToList();

        await Fixture.Client.Streams
            .AppendSession(requests, ct)
            .ShouldNotThrowAsync()
            // .InspectErrorAsync(failure => {
            //     failure. .Count.ShouldBeGreaterThan(0);
            //     Should.NotThrow(() => failures..FirstOrDefault().Error.Throw());
            // })
            .InspectAsync(success => {
                success.Count().ShouldBe(requests.Count);

                for (var i = 0; i < requests.Count; i++) {
                    var request = requests[i];
                    var result  = success[i];

                    var expectedRevision = StreamRevision.From(request.Messages.Count() - 1);

                    result.Stream.ShouldBe(request.Stream);
                    result.StreamRevision.ShouldBe(expectedRevision);
                    result.Position.ShouldBeGreaterThan(LogPosition.Earliest);
                }
            });
    }
}
