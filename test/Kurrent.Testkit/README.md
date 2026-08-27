# Kurrent.Testkit

A testing toolkit for the Kurrent SDK that provides test execution infrastructure, data generation,
advanced assertions, and observability integration.

## Overview

Kurrent.Testkit is an opinionated testing library built on TUnit that standardizes the testing experience
across the Kurrent SDK codebase. It provides:

- **Test Execution**: Custom test executor with structured logging and gRPC error formatting
- **Test Data Generation**: Bogus-based fake data with domain-specific datasets (HomeAutomation, ArcadeRacer)
- **Advanced Assertions**: Deep object graph comparison, conditional assertions, and GUID validation
- **Test Organization**: Test case generators, assembly-level skip attributes, and test context state management
- **Observability**: Serilog logging with OpenTelemetry export and per-test correlation

## Required Setup

Every test project that references Kurrent.Testkit needs a single assembly attribute in a `TestkitBootstrap.cs` file:

```csharp
[assembly: TUnit.Core.Executors.TestExecutor<Kurrent.Testkit.TestkitTestExecutor>]
```

That's it. The `TestkitExecutionContext` static constructor handles all initialization automatically —
configuration loading, Serilog setup, and OpenTelemetry export.
No `[Before(Assembly)]` hooks or manual initialization required.

## Table of Contents

- [Getting Started](#getting-started)
- [Test Execution](#test-execution)
- [Test Data Generation](#test-data-generation)
- [Advanced Assertions](#advanced-assertions)
- [Test Case Generators](#test-case-generators)
- [Test Context Extensions](#test-context-extensions)
- [Skipping Tests](#skipping-tests)
- [Logging & Observability](#logging--observability)
- [Datasets](#datasets)

## Getting Started

### 1. Add Project Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\Kurrent.Testkit\Kurrent.Testkit.csproj" />
</ItemGroup>
```

### 2. Create Bootstrap File

Create a `TestkitBootstrap.cs` file with the single assembly attribute shown in [Required Setup](#required-setup).

### 3. Write Your First Test

```csharp
public class MyFirstTests {
    [Test]
    public async ValueTask my_test_should_pass() {
        // Arrange
        var expected = "Hello, World!";

        // Act
        var actual = "Hello, World!";

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }
}
```

## Test Execution

### TestkitTestExecutor

The custom test executor (`Kurrent.Testkit.TestkitTestExecutor`) wraps every test invocation to provide:

- **Log context injection**: Pushes a `TestUid` property into Serilog's `LogContext` so all log entries
  during a test are tagged with the test's unique identifier
- **gRPC error formatting**: Catches `RpcException` and reformats it into a human-readable message
  with status code, error detail, and status details — instead of a raw gRPC stack trace

### TestkitExecutionContext

The `TestkitExecutionContext` static class initializes once (guarded by `Interlocked.CompareExchange`)
when the test executor first runs. It sets up:

- **Configuration**: Loads `appsettings.json` and environment-specific overrides via `IConfiguration`
- **Serilog**: Console output with `TestUid` correlation, OpenTelemetry OTLP export, and enrichers
  for thread ID, process ID, machine name, and exception details
- **Public properties**: `Configuration`, `LoggerFactory`, and `Logger` are available for test code

## Test Data Generation

### BogusFaker

`BogusFaker` is a thin wrapper over Bogus's `Faker` class, living in the `Bogus` namespace for ergonomic access.
Use it as a TUnit class data source:

```csharp
public class MyTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; init; }

    [Test]
    public async ValueTask generates_random_data() {
        // Arrange
        var name = Faker.Name.FullName();
        var email = Faker.Internet.Email();

        // Act & Assert
        await Assert.That(name).IsNotNull();
    }
}
```

## Advanced Assertions

### Deep Object Graph Equivalency

`IsDeepEquivalentTo` performs recursive property-by-property comparison between two objects:

```csharp
[Test]
public async ValueTask deep_comparison_should_succeed() {
    // Arrange
    var expected = new Order {
        Id = 1,
        Items = [
            new() { ProductId = 100, Quantity = 2 },
            new() { ProductId = 101, Quantity = 1 }
        ]
    };

    // Act
    var actual = GetOrder();

    // Assert
    await Assert.That(actual).IsDeepEquivalentTo(expected, cfg => cfg
        .Excluding<Order>(x => x.CreatedAt)
        .WithStringComparison(StringComparison.OrdinalIgnoreCase)
        .WithNumericTolerance(0.01));
}
```

### GUID Assertion

```csharp
await Assert.That(record.Id).IsGuid();
```

### Equivalency Configuration

The full set of configuration options for `IsDeepEquivalentTo`:

```csharp
cfg => cfg
    // Member selection
    .Excluding<T>(x => x.Property)              // Exclude by expression
    .Excluding("Path.To.Property")              // Exclude by path string
    .ExcludingMembersNamed("CreatedAt", "Id")   // Exclude by member name
    .ExcludingType<DateTime>()                  // Exclude all members of a type
    .Including<T>(x => x.Prop1, x => x.Prop2)  // Include only specific members
    .Including(member => member.Name != "Skip") // Include by predicate
    .ExcludingMissingMembers()                  // Ignore members present only on one side
    .ExcludingNestedObjects()                   // Compare only top-level members

    // Comparison strategy
    .Using<DateTime>((a, e) => a.Date == e.Date)  // Custom comparer for a type
    .ComparingByValue<MyStruct>()               // Use Equals() instead of member comparison
    .ComparingByMembers<MyClass>()              // Use member comparison instead of Equals()
    .ComparingEnumsByName()                     // Compare enums by name, not underlying value

    // String handling
    .WithStringComparison(StringComparison.OrdinalIgnoreCase)
    .IgnoringNewlineStyle()                     // Normalize \r\n vs \n
    .IgnoringLeadingWhitespace()
    .IgnoringTrailingWhitespace()

    // Numeric handling
    .WithNumericTolerance(0.01)

    // Collection ordering
    .IgnoringCollectionOrder()                  // Global: order-independent comparison
    .WithStrictOrdering()                       // Global: order-sensitive comparison
    .WithStrictOrderingFor("Items")             // Path-specific: strict ordering
    .WithoutStrictOrderingFor("Tags")           // Path-specific: ignore ordering

    // Recursion
    .WithMaxRecursionDepth(10)                  // Limit comparison depth
```

Note: `Including` and `Excluding` (path-based) cannot be used together — they are mutually exclusive modes.

### Conditional Assertions

The `When` / `Otherwise` extensions enable branching assertion logic within a single test:

#### Boolean condition on a completed assertion

```csharp
// Skips the assertion entirely when condition is false
await Assert.That(record.Value).IsEquivalentTo(expected).When(!skipDecoding);
```

#### Boolean condition with assertion factory

```csharp
await Assert.That(value)
    .When(!skipDecoding, x => x.IsEquivalentTo(expected))
    .Otherwise(x => x.IsNull());
```

#### Predicate condition

```csharp
await Assert.That(record.Value)
    .When(x => x is not null, x => x.IsEquivalentTo(expected))
    .Otherwise(x => x.IsNull());
```

## Test Case Generators

### TestCaseGenerator

Base classes for providing test case data to parameterized tests. Supports up to 5 type parameters:

```csharp
class ValidStreamNames : TestCaseGenerator<string> {
    protected override IEnumerable<string> Data() => [
        "stream-1",
        "stream-2",
        "category-stream"
    ];
}

[Test]
[MethodDataSource<ValidStreamNames>]
public async ValueTask reads_from_valid_stream(string streamName) {
    // ...
}
```

Multi-parameter variant:

```csharp
class SchemeAndPort : TestCaseGenerator<string, int> {
    protected override IEnumerable<(string, int)> Data() => [
        ("esdb", 2113),
        ("esdb+discover", 2113)
    ];
}
```

### TestCaseDataGenerator

Wraps data in `TestCase<T>` records that support display names, skip reasons, and categories:

```csharp
class EdgeCases : TestCaseDataGenerator<string> {
    protected override IEnumerable<TestCase<string>> Data() => [
        new("", DisplayName: "empty string"),
        new(" ", DisplayName: "whitespace", Skip: "Known bug DEV-1234"),
        new("valid", Categories: ["smoke"])
    ];
}
```

## Test Context Extensions

### State Management

Store and retrieve typed items in the test context's state bag:

```csharp
// Store
context.InjectItem(myFixture);
context.InjectItem(myFixture, key: "custom-key");

// Retrieve (returns null if missing)
var fixture = context.ExtractItem<MyFixture>();

// Retrieve (throws if missing)
var fixture = context.ExtractRequiredItem<MyFixture>();

// Try pattern
if (context.TryExtractItem<MyFixture>(out var fixture)) { ... }

// Check existence
if (context.ContainsItem<MyFixture>()) { ... }
```

### TestUid

Every test context exposes a typed `Guid` identifier:

```csharp
var uid = context.TestUid; // Guid parsed from TestContext.Id
```

This is the same value injected into Serilog's log context by the test executor.

## Skipping Tests

### SkipAllAttribute

Assembly-level attribute for centralized skip management. Apply multiple instances to create a skip registry:

```csharp
// Skip all tests in a specific class
[assembly: SkipAll(typeof(StreamReadTests), "Blocked by DEV-1234")]

// Skip all tests under a namespace prefix
[assembly: SkipAll("Kurrent.Surge.Tests.Integration.Streams", "Server upgrade in progress")]
```

## Logging & Observability

### Test Correlation

The test executor automatically pushes a `TestUid` property into Serilog's `LogContext` for every test.
All log entries during a test are tagged with this identifier, enabling filtering in Seq or other log aggregation tools.

### OpenTelemetry Integration

Configure OTel service metadata on the test context:

```csharp
context.ConfigureOtel(new OtelServiceMetadata("MyService") {
    ServiceVersion   = "1.0.0",
    ServiceNamespace = "Kurrent.Tests"
});
```

Retrieve it later:

```csharp
var metadata = context.GetOtelServiceMetadata();
```

Set just the service name:

```csharp
context.SetOtelServiceName("MyService");
```

`OtelServiceMetadata` also supports environment variable propagation:

```csharp
new OtelServiceMetadata("MyService") {
    ServiceVersion = "1.0.0"
}.UpdateEnvironmentVariables();
// Sets OTEL_RESOURCE_ATTRIBUTES and OTEL_SERVICE_NAME
```

## Datasets

### HomeAutomation

A smart home domain with rooms, devices, and correlated events. Access via the Bogus Faker extension:

```csharp
var home = Faker.HomeAutomation().Home(rooms: 5, devicesPerRoom: 2);
var room = Faker.HomeAutomation().Room(RoomType.Kitchen);
var device = Faker.HomeAutomation().Device(DeviceType.SmartLight);
```

**Device types**: Thermostat, SmartLight, MotionSensor, DoorLock, SmokeDetector, HumiditySensor, WindowSensor, WaterSensor.

**Events** are generated as infinite `IEnumerable<IHomeAutomationEvent>` streams — take what you need:

```csharp
// Generate events for a home
var events = Faker.HomeAutomation().Events(home).Take(50).ToList();

// Generate events for a specific room
var roomEvents = Faker.HomeAutomation().Events(room).Take(20).ToList();

// Generate events for specific devices
var deviceEvents = Faker.HomeAutomation().Events(device1, device2).Take(10).ToList();
```

Events are automatically correlated — motion detection in a room can trigger light state changes in the same room.

### ArcadeRacer

A racing simulation domain with cars, circuits, and race events.
Supports both pre-computed (synchronous) and streaming (asynchronous) modes.

**Pre-computed race** — runs the simulation synchronously and returns a complete logbook:

```csharp
var logbook = Faker.ArcadeRacer().Race
    .WithCars(6)
    .WithLaps(5)
    .WithDifficulty(CircuitDifficulty.Hard)
    .WithConditions(TimeOfDay.Night, Weather.Rain)
    .Simulate();
```

**Streaming race** — produces events on a background task via a bounded channel:

```csharp
await using var race = ArcadeRaceSimulator.NewStreamingRace(Faker)
    .WithCars(8)
    .WithPacing(TimeSpan.FromMilliseconds(100))
    .Start();

await foreach (var evt in race.RaceEvents(includeRaceFinished: true, ct)) {
    // Process events as they arrive
}
```

**Race events**: GridFormed, RaceStarted, LapCompleted, OvertakePerformed, PitStopPerformed,
IncidentOccurred, CarRetired, WeatherChanged, RaceFinished.

**Individual fakers** are also exposed for granular control:

```csharp
var car = Faker.ArcadeRacer().RaceCar.Generate();
var circuit = Faker.ArcadeRacer().Circuit.Generate();
var lapEvent = Faker.ArcadeRacer().RaceEvents.LapCompleted.Generate();
```
