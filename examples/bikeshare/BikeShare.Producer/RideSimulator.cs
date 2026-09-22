using BikeShare.Rides;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BikeShare.Producer;

sealed class RideSimulator(KurrentClient client, ILogger<RideSimulator> logger) : BackgroundService {
    const int     MinutesPerTick = 5;
    const decimal UnlockFee      = 1.00m;
    const decimal FarePerMinute  = 0.15m;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var docked = Enumerable.Range(991, 8).Select(n => n.ToString()).ToList();
        var idle   = new List<string> { "alice", "bruno", "chen", "dana", "emeka", "farah" };
        var rides  = new List<Ride>();
        var tick   = 0;

        logger.LogInformation("{Bikes} bikes docked, {Riders} riders in town", docked.Count, idle.Count);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try {
            while (await timer.WaitForNextTickAsync(stoppingToken)) {
                tick++;

                var canUnlock = docked.Count > 0 && idle.Count > 0;

                if (canUnlock && (rides.Count == 0 || Random.Shared.Next(3) > 0))
                    await Unlock(Pick(docked), Pick(idle), stoppingToken);
                else
                    await Return(Pick(rides), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // A stop is not the end of the rides. Every bike still out comes back below, so the
            // next run never unlocks a bike whose stream says it is already out.
        }

        while (rides.Count > 0)
            await Return(rides[0], CancellationToken.None);

        logger.LogInformation("Closing time, every bike is docked");

        return;

        async Task Unlock(string bike, string rider, CancellationToken ct) {
            var records = new[] {
                AppendRecord.Create(new BikeUnlocked(bike, rider), r => r with { Stream = $"bike-{bike}" }),
                AppendRecord.Create(new RideStarted(rider, bike), r => r with { Stream = $"rider-{rider}" })
            };

            await client.Streams.AppendRecords(records, ct);

            docked.Remove(bike);
            idle.Remove(rider);
            rides.Add(new Ride(bike, rider, tick));

            logger.LogInformation("{Rider} unlocked bike {Bike}", rider, bike);
        }

        async Task Return(Ride ride, CancellationToken ct) {
            var minutes = Math.Max(1, tick - ride.UnlockedAt) * MinutesPerTick;
            var fare    = UnlockFee + minutes * FarePerMinute;

            var records = new[] {
                AppendRecord.Create(new BikeReturned(ride.Bike, ride.Rider, minutes), r => r with { Stream = $"bike-{ride.Bike}" }),
                AppendRecord.Create(new RideEnded(ride.Rider, ride.Bike, minutes, fare), r => r with { Stream = $"rider-{ride.Rider}" })
            };

            await client.Streams.AppendRecords(records, ct);

            rides.Remove(ride);
            docked.Add(ride.Bike);
            idle.Add(ride.Rider);

            logger.LogInformation("{Rider} returned bike {Bike} after {Minutes} min", ride.Rider, ride.Bike, minutes);
        }

        static T Pick<T>(List<T> items) => items[Random.Shared.Next(items.Count)];
    }

    readonly record struct Ride(string Bike, string Rider, int UnlockedAt);
}
