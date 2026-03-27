// // ReSharper disable ArrangeTypeMemberModifiers
//
// using Bogus;
// using Kurrent.Client;
// using Kurrent.Client.Streams;
// using Kurrent.Testing.Containers.KurrentDB;
// using Kurrent.Testing.Testkit;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Time.Testing;
// using Serilog;
// using Testing.ArcadeRacer;
// using TUnit.Core.Interfaces;
//
// using ILogger = Microsoft.Extensions.Logging.ILogger;
//
// namespace Kurrent.Processors.Tests.Fixtures;
//
// public sealed class KurrentClientTestFixture : IAsyncInitializer, IAsyncDisposable {
//     [ClassDataSource<KurrentDBTestContainer>(Shared = SharedType.PerTestSession)]
//     public required KurrentDBTestContainer KurrentDB { get; [UsedImplicitly] init; }
//
//     [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
//     public required BogusFaker Faker { get; [UsedImplicitly] init; }
//
//     /// <summary>
//     /// The logger used for logging in tests.
//     /// </summary>
//     public ILogger Logger => TestkitExecutionContext.Logger;
//
//     /// <summary>
//     /// The logger factory used for creating loggers in tests.
//     /// </summary>
//     public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;
//
//     /// <summary>
//     /// Time provider used for simulating and controlling time in tests.
//     /// </summary>
//     public FakeTimeProvider Time { get; } = new FakeTimeProvider();
//
//     /// <summary>
//     /// The Kurrent client used for testing.
//     /// </summary>
//     public KurrentClient Client { get; private set; } = null!;
//
//     public Task InitializeAsync() {
//        var lazyClient = new Lazy<KurrentClient>(() => {
//             var builder = KurrentClientOptions.Parse(KurrentDB.AuthenticatedConnectionString).GetBuilder()
//                 .WithLoggerFactory(TestkitExecutionContext.LoggerFactory)
//                 .ConfigureSchema(schema => schema
//                     .WithValidation(false)
//                     .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
//                 .ConfigureResilience(KurrentClientResilienceOptions.NoResilience);
//
//             return builder.CreateClient();
//         });
//
//         Client = lazyClient.Value;
//
//         return Task.CompletedTask;
//     }
//
//     public async ValueTask DisposeAsync() =>
//         await Client.DisposeAsync().ConfigureAwait(false);
//
//     #region . Seed Data .
//
//     // with a but of work, we could create a generic test fixture by simply indicating the generic type of the
//     // dataset to use and having some sort of factory that can create the dataset and the corresponding seeded stream
//     // result. For now, we'll just implement specific seeding methods for our arcade racer dataset, which is what
//     // most  of our tests will be using.
//
//     // this works for most of cases, and if the tests requires manual
//
//     public async ValueTask<SeededTestStream> SeedData(int numberOfEvents, CancellationToken cancellationToken) {
//         var data = await ArcadeRacerTestDataSource
//             .SimulateRaceEvents(numberOfEvents)
//             .ConfigureAwait(false);
//
//         var rsp = await Client.Streams
//             .AppendRecords(data.AppendRequest, cancellationToken)
//             .ConfigureAwait(false);
//
//         var seeded = new SeededTestStream(data, rsp.Revisions[0].Revision, rsp.Position);
//
//         Logger.LogDebug(
//             "Seeded {Count} events: {Stream} v{Revision} @ {Position}",
//             seeded.Records.Count, seeded.Stream, seeded.Revision, seeded.Position);
//
//         return seeded;
//     }
//
//     public async ValueTask<SeededTestStream> SeedData(CancellationToken cancellationToken) =>
//         await SeedData(Random.Shared.Next(3, 13), cancellationToken);
//
//     #endregion
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
