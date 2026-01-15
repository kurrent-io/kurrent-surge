# TUnit API Reference

> **Version:** TUnit 1.9.x | **Official Docs:** https://tunit.dev

Complete reference for all TUnit attributes, assertions, and lifecycle management features.

## Lifecycle Attributes

### `[Before]` Attribute

Specifies a method to run before tests at various scopes.

**Syntax:**
```csharp
[Before(HookType hookType)]
public async Task SetupMethod() { }
```

**Hook Types:**
- `HookType.Test` - Runs before each test method
- `HookType.Class` - Runs once before all tests in the class
- `HookType.Assembly` - Runs once before all tests in the assembly

**Examples:**
```csharp
[Before(Test)]
public async Task SetUpEachTest() {
    _database = await TestDatabase.CreateAsync();
    await _database.SeedTestDataAsync();
}

[Before(Class)]
public async Task SetUpClass() {
    _sharedResource = await SharedResource.InitializeAsync();
}

[Before(Assembly)]
public static async Task SetUpAssembly() {
    await GlobalConfiguration.InitializeAsync();
}
```

**Notes:**
- Methods can be `async Task` or `void`
- Class and Assembly-scoped methods should be `static`
- Multiple `[Before]` methods can exist at the same scope

### `[After]` Attribute

Specifies a method to run after tests at various scopes.

**Syntax:**
```csharp
[After(HookType hookType)]
public async Task TeardownMethod() { }
```

**Hook Types:**
- `HookType.Test` - Runs after each test method
- `HookType.Class` - Runs once after all tests in the class
- `HookType.Assembly` - Runs once after all tests in the assembly

**Examples:**
```csharp
[After(Test)]
public async Task TearDownEachTest() {
    await _database.CleanupAsync();
    await _database.DisposeAsync();
}

[After(Class)]
public async Task TearDownClass() {
    await _sharedResource.DisposeAsync();
}

[After(Assembly)]
public static async Task TearDownAssembly() {
    await GlobalConfiguration.CleanupAsync();
}
```

**Notes:**
- Always runs, even if the test fails
- Exceptions in `[After]` methods are reported but don't fail the test
- Useful for cleanup and resource disposal

### `[BeforeEvery]` Attribute

Runs before every test in the hierarchy below the current scope.

**Syntax:**
```csharp
[BeforeEvery(HookType hookType)]
public async Task SetUpEvery() { }
```

**Examples:**
```csharp
// In base class - runs before all derived class tests
[BeforeEvery(Test)]
public virtual async Task SetUpEveryTest() {
    _logger = new TestLogger();
}

// In assembly attribute - runs before all tests in assembly
[assembly: BeforeEvery(Test)]
```

### `[AfterEvery]` Attribute

Runs after every test in the hierarchy below the current scope.

**Syntax:**
```csharp
[AfterEvery(HookType hookType)]
public async Task TearDownEvery() { }
```

## Test Attributes

### `[Test]` Attribute

Marks a method as a test.

**Syntax:**
```csharp
[Test]
public async Task test_name() { }
```

**Requirements:**
- Method must be `public`
- Method must return `Task` or `void`
- Test name should use snake_case convention

**Examples:**
```csharp
[Test]
public async Task create_order_saves_to_database() {
    var order = new Order { CustomerId = "customer-123" };
    await _orderService.CreateOrderAsync(order);
    await Assert.That(await _database.OrderExistsAsync(order.Id)).IsTrue();
}

[Test]
public void validate_email_rejects_invalid_format() {
    var result = EmailValidator.Validate("invalid-email");
    Assert.That(result.IsValid).IsFalse();
}
```

## Data-Driven Testing Attributes

### `[Arguments]` Attribute

Provides inline data for parameterized tests.

**Syntax:**
```csharp
[Arguments(arg1, arg2, arg3, ...)]
```

**Examples:**
```csharp
[Test]
[Arguments("user@example.com", "Password123!")]
[Arguments("admin@example.com", "AdminPass456!")]
public async Task login_succeeds_with_valid_credentials(string email, string password) {
    var result = await _authService.LoginAsync(email, password);
    await Assert.That(result.IsSuccess).IsTrue();
}

[Test]
[Arguments(1, 2, 3)]
[Arguments(10, 20, 30)]
[Arguments(-5, 5, 0)]
public void add_two_numbers_returns_expected_sum(int a, int b, int expected) {
    var result = Calculator.Add(a, b);
    Assert.That(result).IsEqualTo(expected);
}
```

**Notes:**
- Can have multiple `[Arguments]` attributes
- Test runs once per `[Arguments]` attribute
- Arguments must match method parameter types

### `[MethodDataSource]` Attribute

Uses a method to provide test data.

**Syntax:**
```csharp
[MethodDataSource(nameof(MethodName))]
```

**Data Source Method Requirements:**
- Must be `static`
- Must return `IEnumerable<T>` where T is tuple or object array
- Must match test method parameter count

**Examples:**
```csharp
[Test]
[MethodDataSource(nameof(GetOrderTestData))]
public async Task process_order_calculates_correct_total(Order order, decimal expectedTotal) {
    var result = await _orderService.ProcessOrderAsync(order);
    await Assert.That(result.Total).IsEqualTo(expectedTotal);
}

static IEnumerable<(Order, decimal)> GetOrderTestData() {
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

**Using Object Arrays:**
```csharp
[Test]
[MethodDataSource(nameof(GetTestData))]
public void test_with_multiple_parameters(string name, int age, bool isActive) {
    // Test implementation
}

static IEnumerable<object[]> GetTestData() {
    yield return new object[] { "John", 30, true };
    yield return new object[] { "Jane", 25, false };
}
```

### `[MatrixDataSource]` Attribute

Generates combinatorial test cases from multiple data sources.

**Syntax:**
```csharp
[MatrixDataSource(nameof(Source1), nameof(Source2), nameof(Source3), ...)]
```

**Examples:**
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

**Result:**
- Generates 2 × 3 × 2 = 12 test cases
- Tests all combinations of input values

### `[ClassDataSource<T>]` Attribute

Injects instances of type T into test methods (useful for integration testing).

**Syntax:**
```csharp
[ClassDataSource<TType>(Shared = SharedType.PerTestSession)]
```

**Shared Types:**
- `SharedType.None` - New instance per test
- `SharedType.PerTestSession` - Shared across all tests
- `SharedType.PerClass` - Shared within test class

**Examples:**
```csharp
// WebApplicationFactory injection
[Test]
[ClassDataSource<WebApplicationFactory<Program>>(Shared = SharedType.PerTestSession)]
public async Task test_api_endpoint(WebApplicationFactory<Program> factory) {
    using var client = factory.CreateClient();
    var response = await client.GetAsync("/api/orders");
    await Assert.That(response.IsSuccessStatusCode).IsTrue();
}

// Custom test fixture injection
[Test]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.PerClass)]
public async Task test_with_database(DatabaseFixture fixture) {
    await fixture.Database.ExecuteAsync("INSERT INTO Orders...");
}
```

## Test Control Attributes

### `[Timeout]` Attribute

Sets maximum execution time for a test (in milliseconds).

**Syntax:**
```csharp
[Timeout(milliseconds)]
```

**Examples:**
```csharp
[Test]
[Timeout(5000)] // 5 seconds
public async Task api_call_completes_within_timeout() {
    var result = await _apiClient.GetDataAsync();
    await Assert.That(result).IsNotNull();
}

[Test]
[Timeout(100)] // 100ms - performance test
public void fast_operation_completes_quickly() {
    var result = FastCalculation();
    Assert.That(result).IsGreaterThan(0);
}
```

**Notes:**
- Test fails if timeout is exceeded
- Useful for detecting performance regressions
- Does not cancel the test execution immediately

### `[Retry]` Attribute

Retries a test up to N times if it fails.

**Syntax:**
```csharp
[Retry(maxAttempts)]
```

**Examples:**
```csharp
[Test]
[Retry(3)]
public async Task flaky_external_api_call_eventually_succeeds() {
    var result = await _externalApi.GetDataAsync();
    await Assert.That(result).IsNotNull();
}
```

**Notes:**
- Test passes if any attempt succeeds
- All attempts are reported in test results
- Use sparingly - indicates test or code quality issues

### `[Repeat]` Attribute

Runs a test N times.

**Syntax:**
```csharp
[Repeat(count)]
```

**Examples:**
```csharp
[Test]
[Repeat(100)]
public async Task concurrent_access_maintains_thread_safety() {
    await _service.ProcessAsync();
    // Verifies thread safety through repetition
}

[Test]
[Repeat(10)]
public void random_number_generator_produces_valid_range() {
    var number = _rng.Next(1, 100);
    Assert.That(number).IsBetween(1, 99);
}
```

**Notes:**
- Each repetition is a separate test execution
- Useful for testing randomness and concurrency
- All repetitions must pass for test to pass

### `[Skip]` Attribute

Skips a test with an optional reason.

**Syntax:**
```csharp
[Skip(reason)]
```

**Examples:**
```csharp
[Test]
[Skip("Integration test requires production database")]
public async Task integration_test_against_production() {
    // Test implementation
}

[Test]
[Skip("Feature not yet implemented - see issue #123")]
public void test_for_future_feature() {
    // Test implementation
}
```

**Notes:**
- Test is not executed
- Reason is displayed in test results
- Better than commenting out tests

### `[NotInParallel]` Attribute

Prevents a test from running in parallel with other tests.

**Syntax:**
```csharp
[NotInParallel]
```

**Examples:**
```csharp
[Test]
[NotInParallel]
public async Task reset_database_requires_exclusive_access() {
    await _database.ResetAsync();
    await Assert.That(_database.GetOrderCount()).IsEqualTo(0);
}

[Test]
[NotInParallel]
public async Task modify_global_configuration() {
    GlobalConfig.SetValue("key", "value");
    // Test implementation
}
```

**Notes:**
- Ensures test runs in isolation
- Use for tests that modify shared state
- Can impact test execution time

## Parallel Execution Attributes

### `[ParallelLimit]` Attribute

Limits number of parallel test executions.

**Syntax:**
```csharp
[ParallelLimit(maxConcurrency)]
```

**Examples:**
```csharp
[Test]
[ParallelLimit(5)]
public async Task api_call_respects_rate_limit() {
    var result = await _rateLimitedApi.GetDataAsync();
    await Assert.That(result).IsNotNull();
}
```

**Notes:**
- Controls concurrency across all tests with this attribute
- Useful for rate-limited resources
- Does not guarantee exact concurrency

### `[ParallelGroup]` Attribute

Groups tests for coordinated parallel execution.

**Syntax:**
```csharp
[ParallelGroup("group-name")]
```

**Examples:**
```csharp
[Test]
[ParallelGroup("database-operations")]
public async Task test_a_modifies_orders() {
    await _database.ModifyOrdersAsync();
}

[Test]
[ParallelGroup("database-operations")]
public async Task test_b_modifies_orders() {
    await _database.ModifyOrdersAsync();
}
```

**Notes:**
- Tests in same group run together
- Tests in different groups can run in parallel
- Useful for managing shared resources

## Test Dependency Attributes

### `[DependsOn]` Attribute

Specifies test dependencies and execution order.

**Syntax:**
```csharp
[DependsOn(nameof(TestMethod))]
```

**Examples:**
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

**Notes:**
- Dependent test skipped if dependency fails
- Multiple dependencies supported
- Creates execution order guarantee

## Assertion API

### `Assert.That()` - Core Assertion Method

TUnit uses fluent assertion syntax with `Assert.That()`.

**Syntax:**
```csharp
await Assert.That(actual).Assertion();
```

**Notes:**
- All assertions are `async` and should be `await`ed
- Provides detailed failure messages automatically

### Value Assertions

```csharp
// Equality
await Assert.That(value).IsEqualTo(expected);
await Assert.That(value).IsNotEqualTo(unexpected);

// Comparison
await Assert.That(value).IsGreaterThan(5);
await Assert.That(value).IsGreaterThanOrEqualTo(5);
await Assert.That(value).IsLessThan(10);
await Assert.That(value).IsLessThanOrEqualTo(10);
await Assert.That(value).IsBetween(5, 10);

// Boolean
await Assert.That(condition).IsTrue();
await Assert.That(condition).IsFalse();
```

### String Assertions

```csharp
await Assert.That(str).Contains("substring");
await Assert.That(str).DoesNotContain("unexpected");
await Assert.That(str).StartsWith("prefix");
await Assert.That(str).EndsWith("suffix");
await Assert.That(str).Matches(@"regex-pattern");
await Assert.That(str).IsEmpty();
await Assert.That(str).IsNotEmpty();
await Assert.That(str).IsNullOrEmpty();
await Assert.That(str).IsNullOrWhiteSpace();
```

### Collection Assertions

```csharp
// Basic collection operations
await Assert.That(collection).HasCount(5);
await Assert.That(collection).IsEmpty();
await Assert.That(collection).IsNotEmpty();
await Assert.That(collection).HasSingleItem();
await Assert.That(collection).Contains(expectedItem);
await Assert.That(collection).Contains(x => x.Id == "123"); // Predicate
await Assert.That(collection).DoesNotContain(unexpectedItem);
await Assert.That(collection).DoesNotContain(x => x.IsDeleted);

// Predicate-based assertions
await Assert.That(collection).All(x => x.IsValid);
await Assert.That(collection).Any(x => x.Status == Status.Active);

// Ordering
await Assert.That(collection).IsInOrder(); // Ascending
await Assert.That(collection).IsInDescendingOrder();
await Assert.That(collection).IsOrderedBy(x => x.Name);
await Assert.That(collection).IsOrderedByDescending(x => x.CreatedAt);

// Equivalency (order-independent)
await Assert.That(collection).IsEquivalentTo(expectedItems);
await Assert.That(collection).IsNotEquivalentTo(otherItems);

// Structural comparison (deep equality)
await Assert.That(collection).IsStructurallyEqualTo(expected);

// Uniqueness
await Assert.That(collection).HasDistinctItems();
```

### Dictionary Assertions (v1.9+)

```csharp
await Assert.That(dictionary).ContainsKey("key");
await Assert.That(dictionary).DoesNotContainKey("missing");
await Assert.That(dictionary).ContainsValue(expectedValue);
await Assert.That(dictionary).HasCount(3);
await Assert.That(dictionary).IsEmpty();
await Assert.That(dictionary).IsNotEmpty();
```

### Set Assertions (v1.9+)

```csharp
await Assert.That(hashSet).Contains(item);
await Assert.That(hashSet).DoesNotContain(item);
await Assert.That(hashSet).HasCount(5);
await Assert.That(hashSet).IsEmpty();
await Assert.That(hashSet).IsNotEmpty();
await Assert.That(hashSet).IsSubsetOf(superSet);
await Assert.That(hashSet).IsSupersetOf(subSet);
```

### Memory/Span Assertions (v1.9+)

```csharp
await Assert.That(memory).IsNotEmpty();
await Assert.That(memory).HasLength(10);
await Assert.That(memory.ToArray()).Contains(expectedByte);
await Assert.That(readOnlyMemory).IsEquivalentTo(expectedMemory);
```

### AsyncEnumerable Assertions (v1.9+)

```csharp
await Assert.That(asyncStream).Contains(expectedItem);
await Assert.That(asyncStream).HasCount(5);
await Assert.That(asyncStream).IsEmpty();
await Assert.That(asyncStream).IsNotEmpty();
await Assert.That(asyncStream).All(x => x.IsValid);
```

### List Assertions (v1.9+)

```csharp
await Assert.That(list).HasCount(5);
await Assert.That(list).Contains(item);
await Assert.That(list).IsInOrder();
await Assert.That(list).HasDistinctItems();

// ReadOnlyList
await Assert.That(readOnlyList).Contains(item);
await Assert.That(readOnlyList).HasCount(3);
```

### Null Assertions

```csharp
await Assert.That(value).IsNull();
await Assert.That(value).IsNotNull();
```

### Exception Assertions

```csharp
// Assert exception is thrown (or subclass)
await Assert.That(() => ThrowingMethod())
    .Throws<InvalidOperationException>();

// Assert exact exception type (no subclasses)
await Assert.That(() => ThrowingMethod())
    .ThrowsExactly<ArgumentNullException>();

// Assert no exception thrown
await Assert.That(() => SafeMethod())
    .ThrowsNothing();

// Assert exception type at runtime
await Assert.That(() => ThrowingMethod())
    .Throws(typeof(InvalidOperationException));

// Message validation
await Assert.That(() => ThrowingMethod())
    .Throws<ArgumentException>()
    .WithMessage("Expected message"); // Exact match

await Assert.That(() => ThrowingMethod())
    .Throws<ArgumentException>()
    .WithMessageContaining("partial"); // Substring (case-insensitive supported)

await Assert.That(() => ThrowingMethod())
    .Throws<ArgumentException>()
    .WithMessageNotContaining("unexpected");

await Assert.That(() => ThrowingMethod())
    .Throws<ArgumentException>()
    .WithMessageMatching(@"Error code: \d+"); // Regex pattern

// ArgumentException-specific: Parameter name validation
await Assert.That(() => ThrowingMethod())
    .Throws<ArgumentNullException>()
    .WithParameterName("paramName");

// Inner exception tracking
await Assert.That(() => ThrowingMethod())
    .Throws<AggregateException>()
    .WithInnerException<InvalidOperationException>();

// Async delegate assertions
await Assert.That(async () => await ThrowingAsyncMethod())
    .Throws<InvalidOperationException>();
```

### Async Assertions

```csharp
// Assert async operation completes successfully
await Assert.That(async () => await OperationAsync())
    .CompletesWith(() => Task.CompletedTask);

// Assert async operation throws
await Assert.That(async () => await ThrowingOperationAsync())
    .Throws<InvalidOperationException>();
```

### Object Assertions

```csharp
// Object equivalence (deep comparison)
await Assert.That(actualOrder).IsEquivalentTo(expectedOrder);

// With exclusions
await Assert.That(actualOrder).IsEquivalentTo(expectedOrder, options =>
    options.Excluding(o => o.CreatedAt)
           .Excluding(o => o.UpdatedAt));

// Same reference
await Assert.That(obj1).IsSameReferenceAs(obj2);
await Assert.That(obj1).IsNotSameReferenceAs(obj2);

// Type assertions
await Assert.That(obj).IsOfType<Order>();
await Assert.That(obj).IsAssignableTo<IOrder>();
```

## Custom Assertions

You can create custom assertion extensions:

```csharp
public static class CustomAssertions {
    public static async Task ShouldBeValidOrder(this Order order) {
        await Assert.That(order).IsNotNull();
        await Assert.That(order.CustomerId).IsNotNullOrEmpty();
        await Assert.That(order.Amount).IsGreaterThan(0);
        await Assert.That(order.Items).IsNotEmpty();
    }

    public static async Task ShouldHaveStatus(this Order order, OrderStatus expectedStatus) {
        await Assert.That(order.Status).IsEqualTo(expectedStatus);
    }
}

// Usage:
[Test]
public async Task order_is_valid() {
    var order = await _orderService.CreateOrderAsync(...);
    await order.ShouldBeValidOrder();
    await order.ShouldHaveStatus(OrderStatus.Pending);
}
```

## Test Categories and Properties

### `[Category]` Attribute

Groups tests by category.

**Syntax:**
```csharp
[Category("category-name")]
```

**Examples:**
```csharp
[Test]
[Category("Integration")]
public async Task integration_test() { }

[Test]
[Category("Performance")]
public async Task performance_test() { }

[Test]
[Category("Smoke")]
[Category("Critical")]
public async Task critical_smoke_test() { }
```

### `[Property]` Attribute

Adds custom metadata to tests.

**Syntax:**
```csharp
[Property("key", "value")]
```

**Examples:**
```csharp
[Test]
[Property("Database", "Required")]
[Property("Docker", "Required")]
public async Task test_with_metadata() { }
```

## Test Output and Logging

TUnit integrates with `ITestContext` for test output:

```csharp
[Test]
public async Task test_with_logging(ITestContext context) {
    context.WriteLine("Starting test execution");

    var result = await _service.ProcessAsync();

    context.WriteLine($"Result: {result}");

    await Assert.That(result).IsNotNull();
}
```

## Best Practices

### Lifecycle Hook Organization

```csharp
public class WellOrganizedTests {
    // 1. Fields
    OrderService _service;
    TestDatabase _database;

    // 2. Class-level setup
    [Before(Class)]
    public static async Task SetUpClass() {
        // Expensive one-time setup
    }

    // 3. Test-level setup
    [Before(Test)]
    public async Task SetUpTest() {
        // Per-test setup
        _database = await TestDatabase.CreateAsync();
        _service = new OrderService(_database.Connection);
    }

    // 4. Tests
    [Test]
    public async Task test_1() { }

    [Test]
    public async Task test_2() { }

    // 5. Test-level teardown
    [After(Test)]
    public async Task TearDownTest() {
        await _database.DisposeAsync();
    }

    // 6. Class-level teardown
    [After(Class)]
    public static async Task TearDownClass() {
        // One-time cleanup
    }
}
```

### Assertion Patterns

```csharp
// ✅ GOOD: Specific assertions
await Assert.That(order.Status).IsEqualTo(OrderStatus.Pending);
await Assert.That(order.CustomerId).IsEqualTo("customer-123");

// ❌ AVOID: Vague assertions
await Assert.That(order).IsNotNull();
// This doesn't validate the order state
```

### Data-Driven Test Organization

```csharp
// ✅ GOOD: Clear test data generation
static IEnumerable<(Order, decimal)> GetOrderTestData() {
    // Simple case
    yield return (
        new Order { Items = [new(Price: 10.00m, Quantity: 1)] },
        10.00m
    );

    // With discount
    yield return (
        new Order {
            Items = [new(Price: 100.00m, Quantity: 1)],
            DiscountPercent = 10
        },
        90.00m
    );

    // Complex case
    yield return (
        new Order {
            Items = [
                new(Price: 50.00m, Quantity: 2),
                new(Price: 25.00m, Quantity: 4)
            ],
            DiscountPercent = 5,
            ShippingCost = 10.00m
        },
        205.00m // (50*2 + 25*4) * 0.95 + 10
    );
}
```

---

This reference provides comprehensive documentation for all TUnit features. Refer to specific sections when implementing tests with particular requirements.
