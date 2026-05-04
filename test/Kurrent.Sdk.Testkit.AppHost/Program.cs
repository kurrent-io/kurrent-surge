using Kurrent.Sdk.Testkit.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var nodes    = builder.Configuration.GetValue(KurrentDBAppHostArgs.TopologyNodes, 1);
var replicas = builder.Configuration.GetValue(KurrentDBAppHostArgs.TopologyReadOnlyReplicas, 0);
var tls      = builder.Configuration.GetValue(KurrentDBAppHostArgs.SecurityTls, false);

var optionsBuilder = KurrentDBTestOptions.Build
    .WithCluster(nodes)
    .WithTls(tls);

if (replicas > 0)
    optionsBuilder.WithReadOnlyReplicas(replicas);

builder.AddKurrentDB(KurrentDBAppHostArgs.DefaultResourceName, optionsBuilder.Build());

builder.Build().Run();
