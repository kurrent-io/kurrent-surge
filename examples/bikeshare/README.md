# BikeShare

This example simulates a bike share. The producer simulates riders who unlock bikes and return
them. The consumer reads the records and shows which bikes are out.

The example has three projects:

| Project | Function |
|---|---|
| `BikeShare.Rides` | The records and the message catalog. The producer and the consumer reference this library. |
| `BikeShare.Producer` | A host that runs `RideSimulator`. Riders unlock bikes and return them. |
| `BikeShare.Consumer` | A host that runs two processors, `FleetWatch` and `RiderLedger`. |

## What You'll Learn

- How to declare a message catalog one time, in the library that holds the records
- How to write to two streams in one transaction with `AppendRecords`
- How to add two processors to one host with `builder.AddKurrentSurge("kurrentdb", surge => ...)`
- How to filter a subscription by category with `RecordFilter.ByCategory`
- How to write a `MessageProcessor` class that keeps its counts in memory

## How It Works

1. `BikeShare.Rides` declares four records. The `[assembly: MessageCatalog(...)]` attribute scans
   the namespace and makes the catalog. The producer and the consumer get the catalog through the
   library reference. They do not declare it again.
2. `RideSimulator` has 8 bikes and 6 riders. Each second, a rider unlocks a bike or returns a bike.
3. An unlock writes `BikeUnlocked` to the `bike-{id}` stream and `RideStarted` to the
   `rider-{name}` stream. `AppendRecords` writes the two records in one transaction. The server
   keeps the two records, or it keeps none.
4. A return writes `BikeReturned` and `RideEnded`, also in one transaction. One second of the
   simulation is 5 minutes of a ride. The fare is 1.00 plus 0.15 for each minute.
5. `FleetWatch` reads the `bike` category. It counts the bikes that are out.
6. `RiderLedger` reads the `rider` category. It adds the fares of each rider.
7. When you stop the producer, `RideSimulator` returns each bike that is out. Thus, when you start
   the producer again, no stream shows a bike as out.

The two processors keep their counts in memory. When you start the consumer again, the counts
start from zero.

## Prerequisites

- A KurrentDB server on `localhost:2113`
- The .NET 10 SDK

## Running the Example

Start the consumer first. A new subscription gets only new records.

```bash
cd examples/bikeshare/BikeShare.Consumer
dotnet run
```

Then start the producer in a second terminal.

```bash
cd examples/bikeshare/BikeShare.Producer
dotnet run
```

Press `Ctrl+C` in each terminal to stop the host.

If the producer cannot write a record, `RideSimulator` stops and the host stops with it.

## Key Code

### BikeShare.Rides/Events.cs

```csharp
[assembly: MessageCatalog("BikeShare.Rides", AlwaysActive = true)]

namespace BikeShare.Rides;

public record BikeUnlocked(string BikeId, string RiderId);
public record RideStarted(string RiderId, string BikeId);

public record BikeReturned(string BikeId, string RiderId, int Minutes);
public record RideEnded(string RiderId, string BikeId, int Minutes, decimal Fare);
```

### BikeShare.Producer/RideSimulator.cs

```csharp
var records = new[] {
    AppendRecord.Create(new BikeUnlocked(bike, rider), r => r with { Stream = $"bike-{bike}" }),
    AppendRecord.Create(new RideStarted(rider, bike), r => r with { Stream = $"rider-{rider}" })
};

await client.Streams.AppendRecords(records, ct);
```

### BikeShare.Consumer/Program.cs

```csharp
builder.AddKurrentSurge("kurrentdb", surge => surge
    .AddProcessor<FleetWatch>(opts =>
        opts.Subscription.Filter = RecordFilter.ByCategory("bike"))
    .AddProcessor<RiderLedger>(opts =>
        opts.Subscription.Filter = RecordFilter.ByCategory("rider")));

await builder.Build().RunAsync();

sealed class FleetWatch : MessageProcessor {
    readonly HashSet<string> _out = [];

    public FleetWatch() {
        Process<BikeUnlocked>((ride, ctx) => {
            _out.Add(ride.BikeId);
            ctx.Logger.LogInformation("Bike {Id} is out with {Rider}, {Out} out", ride.BikeId, ride.RiderId, _out.Count);
        });

        Process<BikeReturned>((ride, ctx) => {
            _out.Remove(ride.BikeId);
            ctx.Logger.LogInformation("Bike {Id} is back after {Minutes} min, {Out} out", ride.BikeId, ride.Minutes, _out.Count);
        });
    }
}
```

## Sample Output

The producer:

```
[INF] 8 bikes docked, 6 riders in town
[INF] bruno unlocked bike 994
[INF] alice unlocked bike 993
[INF] bruno returned bike 994 after 10 min
...
[INF] Application is shutting down...
[INF] chen returned bike 993 after 80 min
[INF] bruno returned bike 991 after 5 min
[INF] Closing time, every bike is docked
```

The consumer:

```
[INF] [rider-ledger-5d687ac3] Processing (sessionId: a3380995-25a0-4478-8fab-8c2f55b802be)
[INF] [fleet-watch-17911a7e] Processing (sessionId: 526fd7e2-5ca4-45df-9a5d-66c0218705fb)
[INF] Bike 994 is out with bruno, 1 out
[INF] Bike 993 is out with alice, 2 out
[INF] bruno paid 2.50 for 10 min, 2.50 so far
[INF] Bike 994 is back after 10 min, 1 out
```

## Configuration

`Program.cs` gives no processor ID, so each ID comes from the type name: `fleet-watch` and
`rider-ledger`. The host reads the options of each processor from the
`Kurrent:Surge:Processors:{id}` configuration section before the lambda runs. A value that the
lambda sets replaces the configured value.

Each host reads the connection string from `ConnectionStrings:kurrentdb` in its
`appsettings.json` file.

To use a different server, change `appsettings.json` or set the `ConnectionStrings__kurrentdb`
environment variable.

## Next Steps

- For the minimum code that writes and reads a stream, see `Streams.QuickStart`.
- For the minimum processor host, see `Processor.QuickStart`.
