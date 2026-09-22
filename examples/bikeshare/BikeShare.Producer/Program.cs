using BikeShare.Producer;
using Kurrent.Surge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog();
builder.Services.AddHostedService<RideSimulator>();

builder.AddKurrentSurge("kurrentdb");

await builder.Build().RunAsync();
