#pragma warning disable CA1822 // Mark members as static

// ReSharper disable InconsistentNaming

using System.Collections.Immutable;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Sample.HomeAutomation;
using Serilog;
using AppendRecord = Kurrent.Client.Streams.AppendRecord;

namespace Kurrent.Client.Tests.Fixtures;

public partial class KurrentClientTestFixture {
    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(int numberOfEvents, CancellationToken cancellationToken) {
        var omgRefactor  = HomeAutomationTestData.SimulateHomeActivity(numberOfEvents);

        var request = new AppendRecordsRequest {
            Records = [],
            Checks  = [ConsistencyCheck.Stream(omgRefactor.Stream).DoesNotExist()]
        };

        var response = await Client.Streams.AppendRecords(request, cancellationToken: cancellationToken);

        Log.Debug(
            "Seeded SmartHomeActivity to stream {Stream} with {Count} events with revision {Revision} at position {Position}",
            request.Checks[0].AsStreamStateCheck.Stream, request.Records.Length, response.Revisions[0].Revision, response.Position);

        return new SeededSmartHomeActivity(omgRefactor, response.Revisions[0].Revision, response.Position);
    }

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(CancellationToken cancellationToken) =>
        await SeedSmartHomeActivity(Random.Shared.Next(3, 13), cancellationToken);

    // /// <summary>
    // /// Creates a unique stream name for KurrentDB operations, combining the given category with a short unique identifier.
    // /// </summary>
    // /// <param name="category">The category associated with the stream name.</param>
    // /// <returns>A StreamName instance containing the generated stream name.</returns>
    // public string NewStreamName([CallerMemberName] string category = "") => $"{category}-{Guid.NewGuid():N}";
    //
    // public CallCredentials CreateCallCredentials((string Username, string Password) credentials) {
    //     var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.Username}:{credentials.Password}"));
    //     return CallCredentials.FromInterceptor((_, metadata) => {
    //         metadata.Add(new Metadata.Entry("Authorization", $"Basic {token}"));
    //         return Task.CompletedTask;
    //     });
    // }
    //
    // public CallCredentials AdminCredentials   => CreateCallCredentials(("admin", "changeit"));
    // public CallCredentials DefaultCredentials => CreateCallCredentials(default);
}

public record SeededSmartHomeActivity(SmartHomeActivity Activity, Revision Revision, LogPosition Position) {
    public SmartHome                   Home          => Activity.Home;
    public StreamName                  Stream        => Activity.Stream;
    public ImmutableList<AppendRecord> Records       => Activity.Records;
    public long                        LastTimestamp => Activity.LastTimestamp;

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        Activity.SimulateMoreEvents(numberOfEvents);

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        Activity.WithExpectedRevision(expectedRevision);

    public static implicit operator AppendRecordsRequest(SeededSmartHomeActivity _) => _.Activity;
}
