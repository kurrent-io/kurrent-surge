#pragma warning disable CA1822 // Mark members as static

// ReSharper disable InconsistentNaming

using System.Collections.Immutable;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Sample.HomeAutomation;

using AppendRecord = Kurrent.Client.Streams.AppendRecord;

namespace Kurrent.Client.Tests.Fixtures;

public partial class KurrentClientTestFixture {
    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(int numberOfEvents, CancellationToken cancellationToken) {
        var request  = HomeAutomationTestData.SimulateHomeActivity(numberOfEvents);
        var response = await Client.Streams.AppendStreamAsync(request, cancellationToken: cancellationToken);
        return new SeededSmartHomeActivity(request, response.StreamRevision, response.Position);
    }

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(CancellationToken cancellationToken) =>
        await SeedSmartHomeActivity(Random.Shared.Next(5, 15), cancellationToken);

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

public record SeededSmartHomeActivity(SmartHomeActivity Activity, StreamRevision StreamRevision, LogPosition Position) {
    public SmartHome                   Home          => Activity.Home;
    public StreamName                  Stream        => Activity.Stream;
    public ImmutableList<AppendRecord> Records       => Activity.Messages;
    public long                        LastTimestamp => Activity.LastTimestamp;

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        Activity.SimulateMoreEvents(numberOfEvents);

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        Activity.WithExpectedRevision(expectedRevision);

    public static implicit operator AppendStreamRequest(SeededSmartHomeActivity _) => _.Activity;
}
