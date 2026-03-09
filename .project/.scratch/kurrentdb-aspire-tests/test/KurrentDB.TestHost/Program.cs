var builder = DistributedApplication.CreateBuilder(args);

// if a connection string is already provided (e.g. from a running instance),
// just reference it. otherwise, spin up a container.
if (string.IsNullOrEmpty(builder.Configuration["ConnectionStrings:kurrentdb"])) {
    builder.AddKurrentDB("kurrentdb");
} else {
    builder.AddConnectionString("kurrentdb");
}

// add more infrastructure here as needed:
// if (string.IsNullOrEmpty(builder.Configuration["ConnectionStrings:kafka"])) {
//     builder.AddKafka("kafka");
// } else {
//     builder.AddConnectionString("kafka");
// }

builder.Build().Run();
