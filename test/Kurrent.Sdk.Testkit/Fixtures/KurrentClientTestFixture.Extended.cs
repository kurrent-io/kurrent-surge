// #pragma warning disable CA1822 // Mark members as static
//
// // ReSharper disable InconsistentNaming
//
// using Kurrent.Client.Streams;
// using Microsoft.Extensions.Logging;
// using Serilog;
// using Testing.ArcadeRacer;
//
// namespace Kurrent.Client.Tests.Fixtures;
//
// public partial class KurrentClientTestFixture {
//
//     [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
//     public required ITestDataSource TestDataSource { get; [UsedImplicitly] init; }
//
//     public async ValueTask<SeededTestStream> SeedRace(int numberOfEvents, CancellationToken cancellationToken) {
//         var ds = await TestDataSource.GenerateSet(numberOfEvents, cancellationToken);
//
//         var rsp = await Client.Streams
//             .AppendRecords(ds.AppendRequest, cancellationToken);
//
//         var seeded = new SeededTestStream(ds, rsp.Revisions[0].Revision, rsp.Position);
//
//         Log.Debug(
//             "Seeded {Count} {DataSource} events: {Stream} v{Revision} @ {Position}",
//             seeded.Records.Count, TestDataSource.Name, seeded.Stream, seeded.Revision, seeded.Position);
//
//         return seeded;
//     }
//
//     public async ValueTask<SeededTestStream> SeedRace(CancellationToken cancellationToken) =>
//         await SeedRace(Random.Shared.Next(15, 60), cancellationToken);
//
//     // public CallCredentials CreateCallCredentials((string Username, string Password) credentials) {
//     //     var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.Username}:{credentials.Password}"));
//     //     return CallCredentials.FromInterceptor((_, metadata) => {
//     //         metadata.Add(new Metadata.Entry("Authorization", $"Basic {token}"));
//     //         return Task.CompletedTask;
//     //     });
//     // }
//     //
//     // public CallCredentials AdminCredentials   => CreateCallCredentials(("admin", "changeit"));
//     // public CallCredentials DefaultCredentials => CreateCallCredentials(default);
// }
//
// public static class ClientTestDataExtensions {
//     public static async ValueTask<SeededTestStream> SeedData(this KurrentClient client, int numberOfEvents, CancellationToken cancellationToken)  {
//         var data = await ArcadeRacerTestDataSource
//             .SimulateRaceEvents(numberOfEvents)
//             .ConfigureAwait(false);
//
//         var rsp = await client.Streams
//             .AppendRecords(data.AppendRequest, cancellationToken)
//             .ConfigureAwait(false);
//
//         var seeded = new SeededTestStream(data, rsp.Revisions[0].Revision, rsp.Position);
//
//         Log.Debug(
//             "Seeded {Count} events: {Stream} v{Revision} @ {Position}",
//             seeded.Records.Count, seeded.Stream, seeded.Revision, seeded.Position);
//
//         return seeded;
//     }
//
//     public static async ValueTask<SeededTestStream> SeedData(this KurrentClient client, CancellationToken cancellationToken) =>
//         await client.SeedData(Random.Shared.Next(3, 13), cancellationToken);
// }
