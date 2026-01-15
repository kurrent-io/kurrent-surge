---
name: tunit-master
description: Write, run, and debug tests using TUnit framework for this .NET project. Activates for test creation, test infrastructure setup, fixing failing tests, or when test coverage is needed. Covers fluent async assertions, test variants, extension points, Docker container testing (Testcontainers), database testing (EF Core, Respawn), and message queue testing. Triggers on "write tests", "add tests", "test this", "run tests", "fix test", "integration test", "TUnit", "property-based testing". Note - This project uses TUnit (not xUnit/NUnit).
---

# TUnit Master - Complete Testing Framework Mastery

> **Official Documentation**: https://tunit.dev

## Overview

**TUnit Master** provides comprehensive expertise in the TUnit testing framework for .NET (v1.9.x), enabling creation and execution of tests ranging from simple unit tests to complex integration scenarios involving web servers, Docker containers, databases, message queues, and cloud services. This skill covers all TUnit attributes, lifecycle management, data-driven testing, parallel execution orchestration, and infrastructure automation patterns.

**Nothing is beyond the grasp of this skill** - from basic assertions to multi-container integration tests with complex dependencies and teardown automation.

## Quick Start

### Installation

**Using Templates (Recommended):**

```bash
dotnet new install TUnit.Templates
dotnet new TUnit -n "YourProjectName"
```

**Manual Setup:**

```bash
dotnet new console --name YourTestProject
cd YourTestProject
dotnet add package TUnit --prerelease
```

Remove any auto-generated `Program.cs` - TUnit handles this automatically.

### Automatic Global Usings

TUnit automatically enables global usings for three key namespaces:

- `TUnit.Core` - provides `[Test]` attribute and lifecycle hooks
- `TUnit.Assertions` - provides `Assert.That()`
- `TUnit.Assertions.Extensions` - provides assertion methods

You can write tests without explicit using statements. Explicit imports shown in examples are for clarity.

### ⚠️ Important: Incompatible Packages

**Do NOT install these packages** - they conflict with TUnit's Microsoft.Testing.Platform foundation:

- ❌ `coverlet.collector`
- ❌ `coverlet.msbuild`
- ❌ `Microsoft.NET.Test.Sdk`

TUnit includes `Microsoft.Testing.Extensions.CodeCoverage` automatically for coverage support.

## Key Capabilities

This skill enables:
- **Unit testing** - TUnit fundamentals, fluent async assertions, data-driven tests, lifecycle hooks
- **Integration testing** - WebApplicationFactory, real database testing, API endpoint testing
- **Container testing** - Testcontainers for PostgreSQL, Redis, RabbitMQ, Azurite, and custom images
- **Database testing** - EF Core in-memory, real databases with Respawn cleanup, SQL Server/PostgreSQL
- **Message queue testing** - RabbitMQ integration, event publishing verification
- **Test orchestration** - Parallel execution control, test dependencies, retry/timeout handling
- **Test variants** - Runtime-generated tests for property-based testing, chaos engineering, mutation testing
- **Extension points** - ITestExecutor, IHookExecutor, event receivers for custom behavior
- **CLI optimization** - Efficient test execution with filtering, TDD workflow support
- **AOT compilation** - Native AOT support for faster test discovery and execution

## Detailed Reference

### 0. Test Execution and CLI Optimization

**Efficient test execution during development and CI/CD:**

TUnit tests can be executed using `dotnet run` against the test project, which provides direct access to TUnit's CLI options for optimized execution workflows.

**Basic Execution Pattern:**

```bash
# Standard execution with framework targeting
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- [TUnit CLI options]
```

**Development-Optimized Execution:**

```bash
# Fast feedback: Run specific test with clean output
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo --fail-fast \
  --treenode-filter "/*/*/*/my_specific_test"

# Run all tests in a test class
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo --fail-fast \
  --treenode-filter "/*/*/OrderServiceTests/*"

# CI/CD optimized execution (clean logs, no colors)
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo --no-ansi --no-progress
```

**Key CLI Options:**

| Flag                       | Purpose                                     | Use Case                                       |
|----------------------------|---------------------------------------------|------------------------------------------------|
| `--disable-logo`           | Suppress TUnit ASCII banner                 | Clean output for CI/CD and focused development |
| `--fail-fast`              | Stop on first test failure                  | Rapid feedback during TDD cycles               |
| `--treenode-filter`        | Pattern-based test selection                | Target specific tests, classes, or namespaces  |
| `--filter-uid`             | Filter by specific test UIDs                | Precise test targeting                         |
| `--no-ansi`                | Remove ANSI color codes                     | CI/CD logs, file output redirection            |
| `--no-progress`            | Minimal progress reporting                  | Reduced output verbosity                       |
| `--maximum-parallel-tests` | Control parallel test execution             | Resource-constrained environments              |
| `--parallelism-strategy`   | `fixed` or `adaptive` (default)             | Fine-tune parallel execution behavior          |
| `--timeout`                | Global test timeout (e.g., `30000` for 30s) | Prevent hanging tests                          |
| `--list-tests`             | List all available tests                    | Test discovery and verification                |

**TreeNode Filter Pattern Examples:**

```bash
# Single specific test
--treenode-filter "/*/*/*/create_order_persists_to_database"

# All tests in a specific class
--treenode-filter "/*/*/OrderServiceTests/*"

# All tests in a namespace pattern
--treenode-filter "/*/*IntegrationTests*/*"

# Wildcard matching for test name patterns
--treenode-filter "/*/*/*order*"

# Multiple levels of wildcards
--treenode-filter "/*/*Order*/*create*"
```

**Development Workflow Optimization (TDD Cycles):**

```bash
# RED phase: Run new failing test quickly
dotnet run --project test/MyApp.Tests \
  --framework net8.0 \
  -- --disable-logo --fail-fast \
  --treenode-filter "/*/*/*/new_feature_should_work"

# GREEN phase: Verify fix works
dotnet run --project test/MyApp.Tests \
  --framework net8.0 \
  -- --disable-logo \
  --treenode-filter "/*/*/*/new_feature_should_work"

# REFACTOR phase: Run all related tests
dotnet run --project test/MyApp.Tests \
  --framework net8.0 \
  -- --disable-logo \
  --treenode-filter "/*/*/FeatureServiceTests/*"

# Final verification: Run full test suite
dotnet run --project test/MyApp.Tests \
  --framework net8.0 \
  -- --disable-logo --fail-fast
```

**CI/CD Pipeline Integration:**

```bash
# GitHub Actions / Azure DevOps / GitLab CI
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo --no-ansi --no-progress --fail-fast

# With explicit test output for CI logs
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo --no-ansi --output Detailed

# Resource-constrained CI environments
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo --no-ansi \
  --maximum-parallel-tests 2 \
  --timeout 60000
```

**Debugging Individual Tests:**

```bash
# Run single test with full output for debugging
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo \
  --treenode-filter "/*/*/*/complex_integration_test" \
  --output Detailed

# List all available tests for verification
dotnet run --project test/MyApp.Tests/MyApp.Tests.csproj \
  --framework net8.0 \
  -- --disable-logo --list-tests
```

**Performance Tuning:**

```bash
# Adaptive parallelism (default, auto-detects optimal thread count)
dotnet run --project test/MyApp.Tests \
  --framework net8.0 \
  -- --disable-logo --parallelism-strategy adaptive

# Fixed parallelism for predictable resource usage
dotnet run --project test/MyApp.Tests \
  --framework net8.0 \
  -- --disable-logo \
  --parallelism-strategy fixed \
  --maximum-parallel-tests 4

# Sequential execution for debugging race conditions
dotnet run --project test/MyApp.Tests \
  --framework net8.0 \
  -- --disable-logo --maximum-parallel-tests 1
```

### 1. TUnit Framework Fundamentals

**Test Structure and Lifecycle:**

```csharp
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

public class OrderServiceTests {
    OrderService      _orderService;
    TestDatabase      _database;
    ILogger<OrderService> _logger;

    // Runs once before all tests in this class
    [Before(Class)]
    public async Task SetUpClass() {
        _database = await TestDatabase.CreateAsync();
        _logger   = new FakeLogger<OrderService>();
    }

    // Runs before each test method
    [Before(Test)]
    public async Task SetUpTest() {
        await _database.SeedTestDataAsync();
        _orderService = new OrderService(_database.Connection, _logger);
    }

    // Runs after each test method
    [After(Test)]
    public async Task TearDownTest() {
        await _database.CleanupAsync();
    }

    // Runs once after all tests in this class
    [After(Class)]
    public async Task TearDownClass() {
        await _database.DisposeAsync();
    }

    [Test]
    public async Task create_order_persists_to_database() {
        // Arrange
        var order = new Order { CustomerId = "customer-123", Amount = 99.99m };

        // Act
        var result = await _orderService.CreateOrderAsync(order);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.OrderId).IsNotNull();

        var persisted = await _database.GetOrderAsync(result.OrderId);
        await Assert.That(persisted).IsNotNull();
        await Assert.That(persisted.CustomerId).IsEqualTo(order.CustomerId);
    }
}
```

**Lifecycle Hook Scopes:**
- `[Before(Test)]` / `[After(Test)]` - Per test method
- `[Before(Class)]` / `[After(Class)]` - Per test class (once before/after all tests)
- `[Before(Assembly)]` / `[After(Assembly)]` - Per test assembly (global setup/teardown)
- `[BeforeEvery(Test)]` / `[AfterEvery(Test)]` - Runs for every test in the hierarchy

### 2. TUnit Assertion Patterns

**TUnit uses `Assert.That()` fluent async syntax:**

The `await` keyword is responsible for performing the assertion - before you call `await`, you're building a chain of assertion rules. This design prevents silent test failures through analyzer warnings.

```csharp
// Value assertions
await Assert.That(result.IsSuccess).IsTrue();
await Assert.That(order.Status).IsEqualTo(OrderStatus.Pending);
await Assert.That(price).IsGreaterThan(0);
await Assert.That(discount).IsBetween(0, 100);

// String assertions
await Assert.That(customerName).Contains("John");
await Assert.That(email).StartsWith("user@");
await Assert.That(orderId).Matches(@"^ORD-\d{6}$");

// Collection assertions
await Assert.That(orders).HasCount(5);
await Assert.That(items).Contains(expectedItem);
await Assert.That(tags).IsEmpty();
await Assert.That(results).All(r => r.IsValid);
await Assert.That(list).IsInOrder();
await Assert.That(items).HasDistinctItems();

// Set/Dictionary assertions (v1.9+)
await Assert.That(hashSet).Contains(item);
await Assert.That(dictionary).ContainsKey("key");
await Assert.That(dictionary).ContainsValue(expectedValue);

// Memory/Span assertions (v1.9+)
await Assert.That(memory).IsNotEmpty();
await Assert.That(span.ToArray()).Contains(expectedByte);

// AsyncEnumerable assertions (v1.9+)
await Assert.That(asyncStream).Contains(expectedItem);

// Null assertions
await Assert.That(customer).IsNotNull();
await Assert.That(cancellationReason).IsNull();

// Exception assertions
await Assert.That(() => service.ProcessOrder(null))
    .ThrowsExactly<ArgumentNullException>()
    .WithMessage("Value cannot be null. (Parameter 'order')");

// Async delegate assertions
await Assert.That(async () => await service.GetOrderAsync(orderId))
    .ThrowsNothing();

// Complex object assertions
await Assert.That(order).IsEquivalentTo(expectedOrder, options =>
    options.Excluding(o => o.CreatedAt));
```

**Assertion Chaining with `.And`:**

Chain multiple assertions on the same value for readable multi-step validations:

```csharp
await Assert.That(result)
    .IsNotNull()
    .And.IsPositive()
    .And.IsEqualTo(3);

await Assert.That(name)
    .StartsWith("Order-")
    .And.HasLength(12)
    .And.Matches(@"^Order-\d{6}$");
```

**Capturing Assertion Results:**

Awaited assertions return the subject, enabling type-safe value capture:

```csharp
// Capture typed result for subsequent operations
var circle = await Assert.That(shape).IsTypeOf<Circle>();
var area = Math.PI * circle.Radius * circle.Radius;

var validOrder = await Assert.That(order).IsNotNull();
await Assert.That(validOrder.Items).HasCount(3);
```

**Multiple Assertions (Assert.Multiple):**

Run multiple independent assertions without early termination - all assertions execute before reporting failures:

```csharp
await using (Assert.Multiple())
{
    await Assert.That(user.Name).IsNotEmpty();
    await Assert.That(user.Email).Contains("@");
    await Assert.That(user.Age).IsGreaterThan(0);
    await Assert.That(user.CreatedAt).IsLessThan(DateTime.UtcNow);
}
// All failures reported together, not just the first one
```

**AssertionGroup for Complex Boolean Logic:**

When mixing `And`/`Or` operators, use `AssertionGroup` to clarify evaluation order:

```csharp
// Complex conditions: (A AND B) OR (C AND D)
var condition1 = AssertionGroup.For(value)
    .WithAssertion(assert => assert.Contains('A'))
    .And(assert => assert.Contains('B'));

var condition2 = AssertionGroup.For(value)
    .WithAssertion(assert => assert.Contains('C'))
    .And(assert => assert.Contains('D'));

await AssertionGroup.Assert(condition1).Or(condition2);
```

### 3. TestContext (v1.9+ Interface)

**Accessing Test Information:**

```csharp
[Test]
public async Task test_with_context_access() {
    var context = TestContext.Current;

    // Access test metadata
    var testName = context?.TestDetails.TestName;
    var className = context?.TestDetails.TestClass.Name;

    // Check custom properties
    if (context?.TestDetails.CustomProperties.ContainsKey("Environment") == true) {
        var env = context.TestDetails.CustomProperties["Environment"];
    }

    // Write output for debugging
    context?.OutputWriter.WriteLine($"Running test: {testName}");

    await Assert.That(true).IsTrue();
}
```

**Test Results Access (AfterEachTest only):**

```csharp
[After(Test)]
public async Task cleanup_with_results(TestContext context) {
    // Results property only available in After hooks
    var result = context.Result;

    if (result?.Status == Status.Failed) {
        // Log failure details, capture screenshots, etc.
        await CaptureDebugInfoAsync(context);
    }
}
```

**TestBuilderContext for Discovery Phase:**

During test discovery, use `TestBuilderContext.Current` instead:

```csharp
public static IEnumerable<TestCase> GetTestData() {
    // Access discovery-phase context
    var builderContext = TestBuilderContext.Current;

    // StateBag carries data to execution-time TestContext
    builderContext?.StateBag["DiscoveryTime"] = DateTime.UtcNow;

    yield return new TestCase { /* ... */ };
}
```

### 4. Data-Driven Testing

**Simple Arguments:**

```csharp
[Test]
[Arguments("user@example.com", "ValidPass123!")]
[Arguments("admin@example.com", "AdminPass456!")]
[Arguments("guest@example.com", "GuestPass789!")]
public async Task login_succeeds_with_valid_credentials(string email, string password) {
    var result = await _authService.LoginAsync(email, password);
    await Assert.That(result.IsSuccess).IsTrue();
}
```

**Method Data Source:**

> **AOT Requirement:** Method data sources **must be static** for AOT compatibility.

```csharp
[Test]
[MethodDataSource(nameof(GetTestOrders))]
public async Task process_order_calculates_correct_total(Order order, decimal expectedTotal) {
    var result = await _orderService.ProcessOrderAsync(order);
    await Assert.That(result.Total).IsEqualTo(expectedTotal);
}

// Must be static for AOT compatibility
static IEnumerable<(Order, decimal)> GetTestOrders() {
    yield return (
        new Order { Items = [new(Price: 10.00m, Quantity: 2)] },
        20.00m
    );
    yield return (
        new Order { Items = [new(Price: 15.50m, Quantity: 3)] },
        46.50m
    );
    yield return (
        new Order {
            Items = [new(Price: 100.00m, Quantity: 1)],
            DiscountPercent = 10
        },
        90.00m
    );
}
```

**Reference Types - Use Func<T> for Isolation:**

Wrap reference types in `Func<>` delegates to prevent test interference through shared object mutations:

```csharp
[Test]
[MethodDataSource(nameof(GetCustomerTestCases))]
public async Task validate_customer_checks_all_rules(Func<Customer> customerFactory, bool expectedValid) {
    var customer = customerFactory(); // Fresh instance per test
    var result = await _validator.ValidateAsync(customer);
    await Assert.That(result.IsValid).IsEqualTo(expectedValid);
}

static IEnumerable<(Func<Customer>, bool)> GetCustomerTestCases() {
    yield return (() => new Customer { Name = "John", Email = "john@test.com" }, true);
    yield return (() => new Customer { Name = "", Email = "john@test.com" }, false);
    yield return (() => new Customer { Name = "John", Email = "" }, false);
}
```

**Async Data Sources:**

```csharp
[Test]
[MethodDataSource(nameof(GetAsyncTestData))]
public async Task process_handles_async_loaded_data(TestCase testCase) {
    var result = await _service.ProcessAsync(testCase.Input);
    await Assert.That(result).IsEqualTo(testCase.Expected);
}

static async IAsyncEnumerable<TestCase> GetAsyncTestData([EnumeratorCancellation] CancellationToken ct = default) {
    await foreach (var item in LoadTestDataAsync().WithCancellation(ct)) {
        yield return item;
    }
}
```

**Matrix Data Source (Combinatorial Testing):**

```csharp
[Test]
[MatrixDataSource(
    nameof(GetCustomerTypes),
    nameof(GetOrderAmounts),
    nameof(GetShippingMethods)
)]
public async Task shipping_cost_calculated_correctly(
    CustomerType customerType,
    decimal orderAmount,
    ShippingMethod shippingMethod
) {
    var result = await _shippingService.CalculateShippingAsync(
        customerType, orderAmount, shippingMethod
    );

    await Assert.That(result.Cost).IsGreaterThanOrEqualTo(0);

    // Premium customers get free shipping over $100
    if (customerType == CustomerType.Premium && orderAmount > 100) {
        await Assert.That(result.Cost).IsEqualTo(0);
    }
}

static IEnumerable<CustomerType> GetCustomerTypes() {
    yield return CustomerType.Regular;
    yield return CustomerType.Premium;
}

static IEnumerable<decimal> GetOrderAmounts() {
    yield return 50.00m;
    yield return 100.00m;
    yield return 200.00m;
}

static IEnumerable<ShippingMethod> GetShippingMethods() {
    yield return ShippingMethod.Standard;
    yield return ShippingMethod.Express;
}
```

### 4. Test Control and Orchestration

**Test Dependencies and Ordering:**

```csharp
public class DatabaseMigrationTests {
    [Test]
    public async Task step1_create_schema() {
        await _migrator.CreateSchemaAsync();
        await Assert.That(_database.SchemaExists("orders")).IsTrue();
    }

    [Test]
    [DependsOn(nameof(step1_create_schema))]
    public async Task step2_seed_initial_data() {
        await _migrator.SeedInitialDataAsync();
        await Assert.That(_database.GetOrderCount()).IsGreaterThan(0);
    }

    [Test]
    [DependsOn(nameof(step2_seed_initial_data))]
    public async Task step3_verify_data_integrity() {
        var orders = await _database.GetAllOrdersAsync();
        await Assert.That(orders).All(o => o.CustomerId != null);
    }
}
```

**Parallel Execution Control:**

```csharp
// Run tests in parallel with limit
[Test]
[ParallelLimit(5)]
public async Task process_order_handles_concurrent_requests() {
    var result = await _orderService.ProcessOrderAsync(new Order());
    await Assert.That(result.IsSuccess).IsTrue();
}

// Disable parallelism for specific test
[Test]
[NotInParallel]
public async Task reset_database_requires_exclusive_access() {
    await _database.ResetAsync();
    await Assert.That(_database.GetOrderCount()).IsEqualTo(0);
}

// Group tests for coordinated parallel execution
[Test]
[ParallelGroup("database-operations")]
public async Task test_a_modifies_database() {
    // Tests in same group run together
}

[Test]
[ParallelGroup("database-operations")]
public async Task test_b_modifies_database() {
    // Tests in same group run together
}
```

**Retry and Timeout:**

```csharp
// Retry flaky tests (e.g., external API calls)
[Test]
[Retry(3)]
public async Task external_api_call_eventually_succeeds() {
    var result = await _externalApi.GetDataAsync();
    await Assert.That(result).IsNotNull();
}

// Set timeout for long-running tests
[Test]
[Timeout(5000)] // 5 seconds
public async Task operation_completes_within_timeout() {
    await _service.LongRunningOperationAsync();
}

// Skip tests conditionally
[Test]
[Skip("Integration test disabled in CI")]
public async Task integration_test_against_production() {
    // Test implementation
}
```

### 5. Test Variants (Runtime-Generated Tests)

Test variants are dynamically generated tests created during parent test execution. They inherit the parent's method template and appear as distinct tests in test explorers.

**TestRelationship Enum:**
- `None` - Independent test (no parent relationship)
- `Retry` - Identical re-run after failure
- `Generated` - Pre-execution exploration
- `Derived` - Post-execution analysis (shrinking, mutation, chaos)

**Creating Test Variants:**

```csharp
[Test]
public async Task property_based_test_with_shrinking() {
    var context = TestContext.Current;
    var input = GenerateRandomInput();

    try {
        var result = await _service.ProcessAsync(input);
        await Assert.That(result.IsValid).IsTrue();
    }
    catch (AssertionException) {
        // On failure, create shrunk variants to find minimal failing case
        var shrunkInputs = Shrink(input);
        foreach (var (shrunk, index) in shrunkInputs.Select((s, i) => (s, i))) {
            await context.CreateTestVariant(
                arguments: [shrunk],
                properties: new Dictionary<string, object?> {
                    ["ShrinkAttempt"] = index,
                    ["OriginalInput"] = input
                },
                relationship: TestRelationship.Derived,
                displayName: $"Shrink #{index}: {shrunk}"
            );
        }
        throw;
    }
}
```

**Use Cases for Test Variants:**

| Pattern                | Description                                                      |
|------------------------|------------------------------------------------------------------|
| Property-based shrinking | On failure, create variants with simpler inputs                |
| Mutation testing       | Generate mutant variants with controlled bugs to verify quality  |
| Adaptive stress testing | Increase load progressively based on performance                |
| Chaos engineering      | Inject faults and verify resilience                              |
| Smart retries          | Retry with adjusted parameters to differentiate transient/permanent failures |
| Exploratory fuzzing    | Generate variants on edge case detection                         |

**Event Receiver for Automatic Shrinking:**

```csharp
public class ShrinkOnFailureAttribute : Attribute, ITestEndEventReceiver
{
    public async ValueTask OnTestEnd(TestContext context)
    {
        if (context.Execution.Result?.Status == Status.Failed) {
            var originalArgs = context.Metadata.TestInformation.TestMethodArguments;
            var shrunkCases = GenerateShrunkCases(originalArgs);

            foreach (var shrunk in shrunkCases) {
                await context.CreateTestVariant(
                    arguments: shrunk,
                    relationship: TestRelationship.Derived,
                    displayName: "Shrunk case"
                );
            }
        }
    }
}
```

> **Note:** Test variants require reflection and are **not AOT-compatible**.

### 6. Extension Points

TUnit provides powerful extension interfaces for customizing test execution:

**ITestExecutor - Custom Test Execution:**

Wrap test body execution with custom logic (logging, telemetry, retry, timeout):

```csharp
public class LoggingTestExecutor : ITestExecutor
{
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        var testName = context.Metadata.TestInformation.TestName;
        Console.WriteLine($"Starting: {testName}");
        var sw = Stopwatch.StartNew();

        try {
            await action();
            Console.WriteLine($"Passed: {testName} ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex) {
            Console.WriteLine($"Failed: {testName} - {ex.Message}");
            throw;
        }
    }
}

// Register at assembly, class, or method level
[assembly: TestExecutor<LoggingTestExecutor>]
```

**IHookExecutor - Custom Hook Execution:**

Customize setup/cleanup hooks with specialized methods for each hook type:

```csharp
public class ResourceTrackingHookExecutor : IHookExecutor
{
    public async ValueTask ExecuteBeforeTestHook(
        MethodMetadata hookMethodInfo,
        TestContext context,
        Func<ValueTask> action)
    {
        Console.WriteLine($"Before test hook: {hookMethodInfo.Name}");
        await action();
    }

    public async ValueTask ExecuteAfterTestHook(
        MethodMetadata hookMethodInfo,
        TestContext context,
        Func<ValueTask> action)
    {
        await action();
        Console.WriteLine($"After test hook: {hookMethodInfo.Name}");
    }

    // Similar methods for Class, Assembly, TestSession, TestDiscovery hooks...
}
```

**Event Receivers:**

Subscribe to test lifecycle events by implementing receiver interfaces:

```csharp
public class TelemetryAttribute : Attribute, ITestStartEventReceiver, ITestEndEventReceiver
{
    public ValueTask OnTestStart(TestContext context)
    {
        Telemetry.TrackEvent("TestStarted", context.Metadata.TestInformation.TestName);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTestEnd(TestContext context)
    {
        var duration = context.Execution.Result?.Duration ?? TimeSpan.Zero;
        var status = context.Execution.Result?.Status.ToString() ?? "Unknown";
        Telemetry.TrackMetric("TestDuration", duration.TotalMilliseconds, status);
        return ValueTask.CompletedTask;
    }
}

// Apply to tests
[Test]
[Telemetry]
public async Task tracked_test() { }
```

**Available Event Receivers:**
- `ITestDiscoveryEventReceiver` - Test discovered
- `ITestRegisteredEventReceiver` - Test registered
- `ITestStartEventReceiver` - Test starts execution
- `ITestEndEventReceiver` - Test completes
- `ITestRetryEventReceiver` - Test is retried

**IParallelLimit - Custom Parallel Constraints:**

```csharp
public class DatabaseConnectionLimit : IParallelLimit
{
    public int Limit => 5; // Max 5 concurrent database tests
}

[Test]
[ParallelLimiter<DatabaseConnectionLimit>]
public async Task database_test() { }
```

### 7. Web Server Integration Testing

**WebApplicationFactory Integration:**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

public class OrderApiIntegrationTests {
    WebApplicationFactory<Program> _factory;
    HttpClient                     _client;
    TestDatabase                   _database;

    [Before(Class)]
    public async Task SetUpFactory() {
        _database = await TestDatabase.CreateAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    // Replace real database with test database
                    services.RemoveAll<DbContext>();
                    services.AddDbContext<OrderContext>(options =>
                        options.UseSqlServer(_database.ConnectionString));

                    // Replace external services with fakes
                    services.RemoveAll<IPaymentService>();
                    services.AddSingleton<IPaymentService, FakePaymentService>();
                });
            });

        _client = _factory.CreateClient();
    }

    [After(Class)]
    public async Task TearDownFactory() {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Test]
    public async Task post_order_creates_order_and_returns_201() {
        // Arrange
        var orderRequest = new {
            CustomerId = "customer-123",
            Items = new[] {
                new { ProductId = "prod-1", Quantity = 2, Price = 19.99 }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", orderRequest);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(response.Headers.Location).IsNotNull();

        var createdOrder = await response.Content.ReadFromJsonAsync<OrderResponse>();
        await Assert.That(createdOrder.OrderId).IsNotNull();
        await Assert.That(createdOrder.Status).IsEqualTo("Pending");
    }

    [Test]
    public async Task get_order_returns_404_when_not_found() {
        var response = await _client.GetAsync("/api/orders/non-existent-id");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("customer-123")]
    [Arguments("customer-456")]
    public async Task get_orders_by_customer_filters_correctly(string customerId) {
        // Arrange - seed test data
        await _database.SeedOrdersAsync(customerId, count: 3);
        await _database.SeedOrdersAsync("other-customer", count: 2);

        // Act
        var response = await _client.GetAsync($"/api/orders?customerId={customerId}");
        var orders   = await response.Content.ReadFromJsonAsync<List<OrderResponse>>();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(orders).HasCount(3);
        await Assert.That(orders).All(o => o.CustomerId == customerId);
    }
}
```

### 6. Docker Container Integration Testing

**Testcontainers Pattern:**

```csharp
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;

public class OrderProcessingIntegrationTests {
    PostgreSqlContainer _postgresContainer;
    RedisContainer      _redisContainer;
    RabbitMqContainer   _rabbitMqContainer;

    OrderService           _orderService;
    IConnectionMultiplexer _redis;
    IConnection            _rabbitConnection;

    [Before(Class)]
    public async Task StartContainers() {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();
        await _postgresContainer.StartAsync();

        // Start Redis container
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _redisContainer.StartAsync();

        // Start RabbitMQ container
        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .Build();
        await _rabbitMqContainer.StartAsync();

        // Initialize services with container connection strings
        var dbConnectionString = _postgresContainer.GetConnectionString();
        var redisConnectionString = _redisContainer.GetConnectionString();
        var rabbitConnectionString = _rabbitMqContainer.GetConnectionString();

        _redis = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
        _rabbitConnection = new ConnectionFactory { Uri = new Uri(rabbitConnectionString) }
            .CreateConnection();

        _orderService = new OrderService(
            dbConnectionString,
            _redis,
            _rabbitConnection
        );
    }

    [After(Class)]
    public async Task StopContainers() {
        _redis?.Dispose();
        _rabbitConnection?.Dispose();

        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _rabbitMqContainer.DisposeAsync();
    }

    [Test]
    public async Task order_processing_publishes_to_rabbitmq_and_caches_in_redis() {
        // Arrange
        var order = new Order { CustomerId = "customer-123", Amount = 99.99m };

        // Act
        var result = await _orderService.ProcessOrderAsync(order);

        // Assert - Verify database persistence
        await Assert.That(result.IsSuccess).IsTrue();
        var dbOrder = await _orderService.GetOrderAsync(result.OrderId);
        await Assert.That(dbOrder).IsNotNull();

        // Assert - Verify Redis cache
        var cachedOrder = await _redis.GetDatabase()
            .StringGetAsync($"order:{result.OrderId}");
        await Assert.That(cachedOrder.HasValue).IsTrue();

        // Assert - Verify RabbitMQ message
        using var channel = _rabbitConnection.CreateModel();
        var message = channel.BasicGet("order-events", autoAck: true);
        await Assert.That(message).IsNotNull();
        await Assert.That(message.Body.ToArray()).Contains(result.OrderId);
    }
}
```

**Docker Compose Integration:**

```csharp
public class ComplexIntegrationTests {
    Process _dockerCompose;

    [Before(Class)]
    public async Task StartDockerCompose() {
        // Start docker-compose with test infrastructure
        _dockerCompose = Process.Start(new ProcessStartInfo {
            FileName = "docker-compose",
            Arguments = "-f docker-compose.test.yml up -d",
            WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "test-infrastructure")
        });

        await _dockerCompose.WaitForExitAsync();

        // Wait for services to be healthy
        await WaitForHealthyAsync("http://localhost:5432", timeout: TimeSpan.FromSeconds(30));
        await WaitForHealthyAsync("http://localhost:6379", timeout: TimeSpan.FromSeconds(30));
        await WaitForHealthyAsync("http://localhost:15672", timeout: TimeSpan.FromSeconds(30));
    }

    [After(Class)]
    public async Task StopDockerCompose() {
        var stopProcess = Process.Start(new ProcessStartInfo {
            FileName = "docker-compose",
            Arguments = "-f docker-compose.test.yml down -v",
            WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "test-infrastructure")
        });

        await stopProcess.WaitForExitAsync();
    }

    async Task WaitForHealthyAsync(string url, TimeSpan timeout) {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout) {
            try {
                await client.GetAsync(url);
                return; // Service is healthy
            }
            catch {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException($"Service at {url} did not become healthy within {timeout}");
    }

    [Test]
    public async Task end_to_end_order_processing_through_all_services() {
        // Test implementation using all Docker Compose services
    }
}
```

### 7. Database Testing Patterns

**Entity Framework Core In-Memory Testing:**

```csharp
public class OrderRepositoryTests {
    DbContextOptions<OrderContext> _options;
    OrderContext                   _context;
    OrderRepository                _repository;

    [Before(Test)]
    public void SetUp() {
        _options = new DbContextOptionsBuilder<OrderContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new OrderContext(_options);
        _repository = new OrderRepository(_context);
    }

    [After(Test)]
    public void TearDown() {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task save_order_persists_to_database() {
        // Arrange
        var order = new Order { CustomerId = "customer-123", Amount = 99.99m };

        // Act
        await _repository.SaveAsync(order);

        // Assert
        var saved = await _context.Orders.FindAsync(order.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved.CustomerId).IsEqualTo(order.CustomerId);
    }
}
```

**Real Database Testing with Respawn:**

```csharp
using Respawn;

public class OrderRepositoryIntegrationTests {
    static SqlConnection _connection;
    static Respawner     _respawner;

    OrderRepository      _repository;

    [Before(Class)]
    public static async Task InitializeDatabase() {
        _connection = new SqlConnection("Server=localhost;Database=TestDb;...");
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions {
            TablesToIgnore = ["__EFMigrationsHistory"],
            DbAdapter = DbAdapter.SqlServer
        });
    }

    [Before(Test)]
    public async Task SetUpTest() {
        await _respawner.ResetAsync(_connection);
        _repository = new OrderRepository(_connection);
    }

    [After(Class)]
    public static async Task CloseDatabase() {
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task complex_query_returns_correct_results() {
        // Arrange - seed complex test data
        await SeedOrdersAsync(customerId: "customer-1", count: 5);
        await SeedOrdersAsync(customerId: "customer-2", count: 3);

        // Act
        var orders = await _repository.GetOrdersByCustomerAsync("customer-1");

        // Assert
        await Assert.That(orders).HasCount(5);
        await Assert.That(orders).All(o => o.CustomerId == "customer-1");
    }
}
```

### 8. Message Queue Testing

**RabbitMQ Integration Testing:**

```csharp
public class OrderEventPublisherTests {
    RabbitMqContainer      _rabbitContainer;
    IConnection            _connection;
    OrderEventPublisher    _publisher;
    List<OrderEvent>       _receivedEvents;

    [Before(Class)]
    public async Task SetUpRabbitMq() {
        _rabbitContainer = new RabbitMqBuilder().Build();
        await _rabbitContainer.StartAsync();

        var factory = new ConnectionFactory {
            Uri = new Uri(_rabbitContainer.GetConnectionString())
        };
        _connection = factory.CreateConnection();
        _publisher = new OrderEventPublisher(_connection);

        // Set up test consumer
        _receivedEvents = new List<OrderEvent>();
        var channel = _connection.CreateModel();
        channel.QueueDeclare("order-events", durable: true, exclusive: false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (sender, args) => {
            var @event = JsonSerializer.Deserialize<OrderEvent>(args.Body.ToArray());
            _receivedEvents.Add(@event);
        };
        channel.BasicConsume("order-events", autoAck: true, consumer);
    }

    [After(Class)]
    public async Task TearDownRabbitMq() {
        _connection?.Dispose();
        await _rabbitContainer.DisposeAsync();
    }

    [Test]
    public async Task publish_order_created_event_delivers_to_queue() {
        // Arrange
        var @event = new OrderCreatedEvent { OrderId = "order-123", CustomerId = "customer-456" };

        // Act
        await _publisher.PublishAsync(@event);

        // Wait for message delivery
        await Task.Delay(500);

        // Assert
        await Assert.That(_receivedEvents).HasCount(1);
        await Assert.That(_receivedEvents[0].OrderId).IsEqualTo(@event.OrderId);
    }
}
```

### 9. Cloud Service Testing

**Azure Blob Storage Testing with Azurite:**

```csharp
public class BlobStorageTests {
    GenericContainer     _azuriteContainer;
    BlobServiceClient    _blobClient;
    OrderDocumentService _documentService;

    [Before(Class)]
    public async Task StartAzurite() {
        _azuriteContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite")
            .WithPortBinding(10000, 10000)
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0")
            .Build();

        await _azuriteContainer.StartAsync();

        var connectionString =
            "DefaultEndpointsProtocol=http;" +
            "AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            $"BlobEndpoint=http://localhost:10000/devstoreaccount1;";

        _blobClient = new BlobServiceClient(connectionString);
        await _blobClient.CreateBlobContainerAsync("order-documents");

        _documentService = new OrderDocumentService(_blobClient);
    }

    [After(Class)]
    public async Task StopAzurite() {
        await _azuriteContainer.DisposeAsync();
    }

    [Test]
    public async Task upload_order_document_stores_in_blob_storage() {
        // Arrange
        var orderId = "order-123";
        var documentContent = "Order invoice content"u8.ToArray();

        // Act
        var blobUrl = await _documentService.UploadDocumentAsync(orderId, documentContent);

        // Assert
        await Assert.That(blobUrl).IsNotNull();

        var container = _blobClient.GetBlobContainerClient("order-documents");
        var blob = container.GetBlobClient($"{orderId}/invoice.pdf");

        await Assert.That(await blob.ExistsAsync()).IsTrue();
    }
}
```

### 10. Performance and Load Testing

**Concurrent Load Testing:**

```csharp
public class OrderServiceLoadTests {
    OrderService _orderService;

    [Before(Class)]
    public async Task SetUp() {
        _orderService = await OrderService.CreateAsync();
    }

    [Test]
    public async Task handles_100_concurrent_order_creations_without_errors() {
        // Arrange
        const int concurrentRequests = 100;
        var orders = Enumerable.Range(0, concurrentRequests)
            .Select(i => new Order {
                CustomerId = $"customer-{i}",
                Amount = 99.99m
            })
            .ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = orders.Select(order => _orderService.CreateOrderAsync(order));
        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        await Assert.That(results).HasCount(concurrentRequests);
        await Assert.That(results).All(r => r.IsSuccess);
        await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(5000); // Under 5 seconds
    }

    [Test]
    [Repeat(10)]
    public async Task sustained_load_maintains_performance() {
        var stopwatch = Stopwatch.StartNew();

        var result = await _orderService.CreateOrderAsync(new Order {
            CustomerId = "load-test",
            Amount = 99.99m
        });

        stopwatch.Stop();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(500); // Under 500ms per request
    }
}
```

## Advanced Scenarios

### Multi-Container Integration Test with Dependencies

```csharp
public class CompleteOrderProcessingFlowTests {
    // Infrastructure containers
    PostgreSqlContainer _database;
    RedisContainer      _cache;
    RabbitMqContainer   _messageQueue;
    GenericContainer    _emailService;

    // Application under test
    WebApplicationFactory<Program> _app;
    HttpClient                     _client;

    [Before(Class)]
    public async Task SetUpInfrastructure() {
        // Start all containers in parallel
        var databaseTask = StartDatabaseAsync();
        var cacheTask = StartCacheAsync();
        var queueTask = StartMessageQueueAsync();
        var emailTask = StartEmailServiceAsync();

        await Task.WhenAll(databaseTask, cacheTask, queueTask, emailTask);

        // Configure application with test infrastructure
        _app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureAppConfiguration((context, config) => {
                    config.AddInMemoryCollection(new Dictionary<string, string> {
                        ["ConnectionStrings:Database"] = _database.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = _cache.GetConnectionString(),
                        ["ConnectionStrings:RabbitMq"] = _messageQueue.GetConnectionString(),
                        ["Services:Email:BaseUrl"] = $"http://localhost:{_emailService.GetMappedPublicPort(8080)}"
                    });
                });
            });

        _client = _app.CreateClient();
    }

    async Task StartDatabaseAsync() {
        _database = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .Build();
        await _database.StartAsync();
    }

    async Task StartCacheAsync() {
        _cache = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _cache.StartAsync();
    }

    async Task StartMessageQueueAsync() {
        _messageQueue = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .Build();
        await _messageQueue.StartAsync();
    }

    async Task StartEmailServiceAsync() {
        _emailService = new ContainerBuilder()
            .WithImage("test-email-service:latest")
            .WithPortBinding(8080, true)
            .Build();
        await _emailService.StartAsync();
    }

    [After(Class)]
    public async Task TearDownInfrastructure() {
        await _app.DisposeAsync();

        // Dispose containers in parallel
        await Task.WhenAll(
            _database.DisposeAsync().AsTask(),
            _cache.DisposeAsync().AsTask(),
            _messageQueue.DisposeAsync().AsTask(),
            _emailService.DisposeAsync().AsTask()
        );
    }

    [Test]
    [Timeout(30000)] // 30 seconds for full integration flow
    public async Task complete_order_flow_processes_order_sends_email_caches_result() {
        // Arrange
        var orderRequest = new CreateOrderRequest {
            CustomerId = "customer-123",
            CustomerEmail = "customer@example.com",
            Items = [
                new OrderItem { ProductId = "prod-1", Quantity = 2, Price = 19.99m }
            ]
        };

        // Act - Create order
        var createResponse = await _client.PostAsJsonAsync("/api/orders", orderRequest);
        await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();

        // Assert - Order persisted to database
        var getResponse = await _client.GetAsync($"/api/orders/{createdOrder.OrderId}");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Assert - Order cached in Redis
        var redisDb = ConnectionMultiplexer.Connect(_cache.GetConnectionString()).GetDatabase();
        var cachedOrder = await redisDb.StringGetAsync($"order:{createdOrder.OrderId}");
        await Assert.That(cachedOrder.HasValue).IsTrue();

        // Assert - Order event published to RabbitMQ
        var rabbitConnection = new ConnectionFactory {
            Uri = new Uri(_messageQueue.GetConnectionString())
        }.CreateConnection();
        using var channel = rabbitConnection.CreateModel();
        var message = channel.BasicGet("order-events", autoAck: true);
        await Assert.That(message).IsNotNull();

        // Assert - Email sent via email service
        var emailClient = new HttpClient {
            BaseAddress = new Uri($"http://localhost:{_emailService.GetMappedPublicPort(8080)}")
        };
        var sentEmails = await emailClient.GetFromJsonAsync<List<Email>>("/api/emails/sent");
        await Assert.That(sentEmails).Any(e =>
            e.To == orderRequest.CustomerEmail &&
            e.Subject.Contains(createdOrder.OrderId)
        );
    }
}
```

## Migration from Other Frameworks

TUnit provides automated migration tooling for xUnit, NUnit, and MSTest.

### Automated Migration

Run the code fixer to automatically migrate most test patterns:

```bash
dotnet format analyzers --severity info --diagnostics TUXU0001
```

This handles approximately **80-90% of typical test suites** automatically.

### xUnit to TUnit

| xUnit | TUnit |
|-------|-------|
| `[Fact]` | `[Test]` |
| `[Theory]` | `[Test]` |
| `[InlineData(...)]` | `[Arguments(...)]` |
| `[MemberData(nameof(...))]` | `[MethodDataSource(nameof(...))]` |
| `[Trait("key", "value")]` | `[Property("key", "value")]` |
| `IClassFixture<T>` | `[ClassDataSource<T>(Shared = SharedType.PerClass)]` |
| `[Collection("name")]` | `[ClassDataSource<T>(Shared = SharedType.Keyed, Key = "name")]` |
| `Assert.Equal(expected, actual)` | `await Assert.That(actual).IsEqualTo(expected)` |

**Note:** All test methods convert to `async Task` with awaited assertions.

### NUnit to TUnit

| NUnit | TUnit |
|-------|-------|
| `[Test]` | `[Test]` |
| `[TestCase(...)]` | `[Arguments(...)]` |
| `[TestCaseSource(nameof(...))]` | `[MethodDataSource(nameof(...))]` |
| `[SetUp]` | `[Before(Test)]` |
| `[TearDown]` | `[After(Test)]` |
| `[OneTimeSetUp]` | `[Before(Class)]` |
| `[OneTimeTearDown]` | `[After(Class)]` |
| `Assert.That(x, Is.EqualTo(y))` | `await Assert.That(x).IsEqualTo(y)` |

### MSTest to TUnit

| MSTest | TUnit |
|--------|-------|
| `[TestMethod]` | `[Test]` |
| `[DataRow(...)]` | `[Arguments(...)]` |
| `[DynamicData(nameof(...))]` | `[MethodDataSource(nameof(...))]` |
| `[TestInitialize]` | `[Before(Test)]` |
| `[TestCleanup]` | `[After(Test)]` |
| `[ClassInitialize]` | `[Before(Class)]` |
| `[ClassCleanup]` | `[After(Class)]` |
| `Assert.AreEqual(expected, actual)` | `await Assert.That(actual).IsEqualTo(expected)` |

### Post-Migration Checklist

1. ✅ Remove `Microsoft.NET.Test.Sdk` package reference
2. ✅ Remove `coverlet.collector` / `coverlet.msbuild`
3. ✅ Update all test methods to `async Task`
4. ✅ Add `await` to all assertions
5. ✅ Verify lifecycle hooks are correctly scoped
6. ✅ Run tests to verify migration success

## AOT Compilation Support

TUnit supports Native AOT compilation for significantly faster test discovery and execution:

```xml
<PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
</PropertyGroup>
```

**AOT Benefits:**
- Faster test discovery (significant speedup)
- Reduced startup time
- Smaller memory footprint

**AOT Limitations:**
- Test variants (`CreateTestVariant`) require reflection and are **not AOT-compatible**
- Dynamic data sources using reflection may not work
- `[DynamicDataSource]` limited in AOT mode

**Best Practices for AOT:**
- Keep data sources lightweight and static
- Use `[Arguments]` and `[MethodDataSource]` over dynamic sources
- Avoid runtime reflection in test setup

## Resources

### Official Documentation

- **TUnit Docs:** https://tunit.dev
- **GitHub:** https://github.com/thomhurst/TUnit
- **LLM Reference:** https://tunit.dev/llms.txt

### Skill References

This skill includes comprehensive reference material for advanced testing scenarios:

**references/**

- `integration_testing_patterns.md` - Detailed patterns for integration testing scenarios
- `docker_testing_guide.md` - Complete guide to Docker container testing with Testcontainers
- `database_testing_strategies.md` - Database testing approaches (in-memory, real databases, cleanup strategies)
- `tunit_api_reference.md` - Complete TUnit attribute and API reference (v1.9.x)

---

**With TUnit Master, nothing is beyond your testing capabilities.** From simple unit tests to complex multi-container integration scenarios with orchestrated dependencies, this skill provides the expertise to test any .NET application comprehensively.
