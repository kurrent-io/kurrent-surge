# Quickstart

<CopyMarkdownButton />

This guide walks through adding `Kurrent.Client` to a .NET console app, connecting to a local KurrentDB node, and appending and reading your first records. It is the fastest path from zero to a running stream.

By the end of this guide, you will have:

- A .NET solution with a **producer** project that appends records and a **consumer** project that reads them.
- A local KurrentDB node running in Docker.
- An interactive loop where the producer writes to a stream and the consumer reads it back.

::: tip Building with AI coding agents?
The [Kurrent agent toolkit](https://github.com/kurrent-io/kurrent-toolkit) gives AI coding agents the tools, knowledge, and guardrails to build on the Kurrent platform.
:::

<div class="steps">

## Install your language runtime

`Kurrent.Client` targets modern .NET. You will need the [.NET SDK 10.0 or later](https://dotnet.microsoft.com/download) installed. Follow the installation instructions for your operating system (Windows, macOS, or Linux) to complete the setup.

## Create the solution

Create a solution with two console projects: a **producer** that appends records and a **consumer** that reads them. Reference `Kurrent.Client` from both.

```bash
dotnet new sln -n Orders
dotnet new console -n Orders.Producer
dotnet new console -n Orders.Consumer
dotnet sln add Orders.Producer Orders.Consumer
dotnet add Orders.Producer package Kurrent.Client
dotnet add Orders.Consumer package Kurrent.Client
```

You now have an `Orders.sln` with `Orders.Producer/` and `Orders.Consumer/` next to it, both referencing `Kurrent.Client`.

## Run KurrentDB locally

Start a single-node KurrentDB instance with [Docker](https://docs.docker.com/get-docker/) and TLS disabled. This is the fastest setup for local development and matches the connection string used below. Any reachable KurrentDB instance works in place of this container.

```bash
docker run --name kurrentdb -d \
    -p 2113:2113 \
    kurrentplatform/kurrentdb:latest \
    --insecure
```

For production deployments, see [Connection Targets](./connect/connection-targets.md) and [Connection Options](./connect/connection-options.md) to set up TLS, authentication, and clustering.

## Append records from the producer

Open `Orders.Producer/Program.cs` and replace its contents. The producer connects to KurrentDB, builds a record with `AppendRecord.New`, and appends it to the `order-123` stream:

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new {
        OrderId  = "order-123",
        Product  = "Widget",
        Quantity = 5
    })
    .Build();

var result = await client.Streams.AppendStream(record);

Console.WriteLine("Appended to {0} at revision {1}", result.Stream, result.Revision);
```

`KurrentClient.Create` parses the connection string and configures a gRPC channel. The client is thread-safe and intended to be reused for the lifetime of your application. `AppendStream` serializes the value as JSON by default and creates the stream automatically if it does not exist.

::: tip
For a production node, omit `?tls=false`. TLS is enabled by default, and the client will refuse to connect to a plaintext endpoint without it.
:::

Run the producer:

```bash
dotnet run --project Orders.Producer
```

You should see something like `Appended to order-123 at revision 0`.

## Read records from the consumer

Open `Orders.Consumer/Program.cs` and replace its contents. The consumer connects with the same connection string, reads every record from `order-123`, and prints each one:

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

var request = ReadStreamRequest.New.ForStream("order-123").Build();

var records = await client.Streams.ReadStream(request)
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord)
    .ToListAsync();

foreach (var record in records) {
    Console.WriteLine("Record: {0} at revision {1}", record.TypeName, record.Revision);
}
```

`ReadStream` returns an `IAsyncEnumerable` that streams results lazily from the server. The LINQ pipeline drops heartbeats and materializes the records into a `List<Record>`.

::: tip
For the simplest case, the `ReadStreamRecords` and `ToListAsync` extensions collapse to one line:

```csharp
var records = await client.Streams.ReadStreamRecords("order-123").ToListAsync();
```
:::

Run the consumer:

```bash
dotnet run --project Orders.Consumer
```

You should see one record printed for the record the producer just appended.

## See it in action

Re-run the producer to append another record:

```bash
dotnet run --project Orders.Producer
```

Then re-run the consumer to see the stream grow:

```bash
dotnet run --project Orders.Consumer
```

Each producer run appends a new record at the next revision, and the consumer prints the full history every time.

::: tip Inspect streams visually
Install [Kurrent Navigator](https://navigator.kurrent.io/), the desktop UI for KurrentDB, and connect it to `localhost:2113`. Open **Stream Browser / order-123** to watch records accumulate as you re-run the producer.
:::

</div>
