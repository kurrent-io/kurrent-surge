using BikeShare.Rides;
using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog();

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

sealed class RiderLedger : MessageProcessor {
    readonly Dictionary<string, decimal> _spent = [];

    public RiderLedger() =>
        Process<RideEnded>((ride, ctx) => {
            var total = _spent[ride.RiderId] = _spent.GetValueOrDefault(ride.RiderId) + ride.Fare;
            ctx.Logger.LogInformation("{Rider} paid {Fare:0.00} for {Minutes} min, {Total:0.00} so far", ride.RiderId, ride.Fare, ride.Minutes, total);
        });
}
