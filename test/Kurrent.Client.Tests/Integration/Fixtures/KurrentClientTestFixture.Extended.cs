#pragma warning disable CA1822 // Mark members as static

// ReSharper disable InconsistentNaming

using Kurrent.Client.Streams;
using Serilog;
using Testing.ArcadeRacer;

namespace Kurrent.Client.Tests.Fixtures;

public partial class KurrentClientTestFixture {
    public async ValueTask<SeededRace> SeedRace(int numberOfEvents, CancellationToken cancellationToken) {
        var race = ArcadeRacerTestData.SimulateRace(numberOfEvents);

        var response = await Client.Streams
            .AppendRecords(race.AppendRequest, cancellationToken);

        Log.Debug(
            "{Stream}: seeded {Count} race events (rev {Revision}, pos {Position})",
            race.Stream, race.Records.Count, response.Revisions[0].Revision, response.Position);

        return new SeededRace(race, response.Revisions[0].Revision, response.Position);
    }

    public async ValueTask<SeededRace> SeedRace(CancellationToken cancellationToken) =>
        await SeedRace(Random.Shared.Next(3, 13), cancellationToken);

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
