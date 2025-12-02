# Database Testing Strategies

Comprehensive guide to database testing strategies, cleanup patterns, and transaction management for .NET integration tests with TUnit.

## Testing Strategy Overview

### In-Memory vs Real Databases

**In-Memory Databases (EF Core InMemory Provider):**
- **Pros**: Fast, no external dependencies, simple setup
- **Cons**: Limited SQL features, no referential integrity, not production-like
- **Use Case**: Unit testing repository logic, simple CRUD operations

**Real Databases (Testcontainers/Docker):**
- **Pros**: Production-like behavior, full SQL features, referential integrity
- **Cons**: Slower startup, requires Docker, more complex setup
- **Use Case**: Integration testing, complex queries, migration testing

## In-Memory Database Testing

### Entity Framework Core InMemory Provider

**Basic Setup:**

```csharp
using Microsoft.EntityFrameworkCore;

public class InMemoryDatabaseTests {
    DbContextOptions<AppDbContext> _options;
    AppDbContext                   _context;

    [Before(Test)]
    public void SetUp() {
        // Use unique database name per test for isolation
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(_options);
    }

    [After(Test)]
    public void TearDown() {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task can_add_and_retrieve_order() {
        // Arrange
        var order = new Order {
            CustomerId = "customer-123",
            OrderDate = DateTime.UtcNow,
            TotalAmount = 99.99m
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = await _context.Orders.FindAsync(order.Id);
        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved.CustomerId).IsEqualTo("customer-123");
    }
}
```

**Seeding Test Data:**

```csharp
public class InMemoryTestsWithSeed {
    DbContextOptions<AppDbContext> _options;
    AppDbContext                   _context;

    [Before(Test)]
    public async Task SetUp() {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(_options);

        await SeedTestDataAsync();
    }

    async Task SeedTestDataAsync() {
        var customers = new[] {
            new Customer { Id = "customer-1", Name = "John Doe", Email = "john@example.com" },
            new Customer { Id = "customer-2", Name = "Jane Smith", Email = "jane@example.com" }
        };

        _context.Customers.AddRange(customers);

        var orders = new[] {
            new Order { CustomerId = "customer-1", OrderDate = DateTime.UtcNow, TotalAmount = 99.99m },
            new Order { CustomerId = "customer-1", OrderDate = DateTime.UtcNow, TotalAmount = 149.99m },
            new Order { CustomerId = "customer-2", OrderDate = DateTime.UtcNow, TotalAmount = 299.99m }
        };

        _context.Orders.AddRange(orders);

        await _context.SaveChangesAsync();
    }

    [Test]
    public async Task get_orders_by_customer_returns_correct_count() {
        var orders = await _context.Orders
            .Where(o => o.CustomerId == "customer-1")
            .ToListAsync();

        await Assert.That(orders).HasCount(2);
    }
}
```

**Limitations:**

```csharp
[Test]
public async Task demonstrates_inmemory_limitations() {
    // InMemory does NOT enforce referential integrity
    var order = new Order {
        CustomerId = "non-existent-customer", // Foreign key not validated
        OrderDate = DateTime.UtcNow,
        TotalAmount = 99.99m
    };

    _context.Orders.Add(order);
    await _context.SaveChangesAsync(); // This succeeds (shouldn't in real DB)

    // InMemory does NOT support:
    // - Raw SQL queries
    // - Stored procedures
    // - Database functions
    // - Complex query features (window functions, CTEs)
}
```

## Real Database Testing

### PostgreSQL with Testcontainers

```csharp
using Testcontainers.PostgreSql;

public class PostgreSqlIntegrationTests {
    PostgreSqlContainer _container;
    AppDbContext        _context;

    [Before(Class)]
    public async Task StartDatabase() {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        await _container.StartAsync();
    }

    [Before(Test)]
    public async Task SetUpTest() {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new AppDbContext(options);

        // Apply migrations
        await _context.Database.MigrateAsync();
    }

    [After(Test)]
    public async Task TearDownTest() {
        // Clean database after each test
        await _context.Database.EnsureDeletedAsync();
        await _context.Database.MigrateAsync();

        await _context.DisposeAsync();
    }

    [After(Class)]
    public async Task StopDatabase() {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task foreign_key_constraint_enforced() {
        var order = new Order {
            CustomerId  = "non-existent-customer",
            OrderDate   = DateTime.UtcNow,
            TotalAmount = 99.99m
        };

        _context.Orders.Add(order);

        // This throws due to foreign key constraint
        await Assert.That(async () => await _context.SaveChangesAsync())
            .Throws<DbUpdateException>();
    }
}
```

### SQL Server with Testcontainers

```csharp
using Testcontainers.MsSql;

public class SqlServerIntegrationTests {
    MsSqlContainer _container;
    AppDbContext   _context;

    [Before(Class)]
    public async Task StartDatabase() {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong!Passw0rd")
            .Build();

        await _container.StartAsync();
    }

    [Before(Test)]
    public async Task SetUpTest() {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();
    }

    [After(Test)]
    public async Task TearDownTest() {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    [After(Class)]
    public async Task StopDatabase() {
        await _container.DisposeAsync();
    }
}
```

## Database Cleanup Strategies

### Strategy 1: EnsureDeleted + Migrate (Slow)

```csharp
[After(Test)]
public async Task CleanupWithDeleteAndMigrate() {
    await _context.Database.EnsureDeletedAsync(); // Drop all tables
    await _context.Database.MigrateAsync();        // Recreate schema
}
```

**Pros**: Complete isolation, clean state
**Cons**: Very slow, recreates entire schema per test
**Use Case**: Small test suites, complex schema changes

### Strategy 2: Respawn (Fast)

Respawn efficiently resets database state by deleting data while preserving schema.

**Installation:**

```bash
dotnet add package Respawn
```

**Setup:**

```csharp
using Respawn;
using Npgsql;

public class RespawnCleanupTests {
    static NpgsqlConnection _connection;
    static Respawner        _respawner;

    AppDbContext _context;

    [Before(Class)]
    public static async Task InitializeDatabase() {
        // Start container and create connection
        var container = new PostgreSqlBuilder().Build();
        await container.StartAsync();

        _connection = new NpgsqlConnection(container.GetConnectionString());
        await _connection.OpenAsync();

        // Apply migrations once
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connection)
            .Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        // Initialize Respawner
        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions {
            TablesToIgnore = ["__EFMigrationsHistory"],
            DbAdapter = DbAdapter.Postgres
        });
    }

    [Before(Test)]
    public async Task SetUpTest() {
        // Reset database to clean state (very fast)
        await _respawner.ResetAsync(_connection);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connection)
            .Options;

        _context = new AppDbContext(options);
    }

    [After(Test)]
    public async Task TearDownTest() {
        await _context.DisposeAsync();
    }

    [After(Class)]
    public static async Task Cleanup() {
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task test_1_has_clean_database() {
        var count = await _context.Orders.CountAsync();
        await Assert.That(count).IsEqualTo(0);

        _context.Orders.Add(new Order { /* ... */ });
        await _context.SaveChangesAsync();
    }

    [Test]
    public async Task test_2_also_has_clean_database() {
        // Database reset between tests
        var count = await _context.Orders.CountAsync();
        await Assert.That(count).IsEqualTo(0);
    }
}
```

**Respawn Configuration:**

```csharp
_respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions {
    // Tables to ignore during reset
    TablesToIgnore = ["__EFMigrationsHistory", "SeedData"],

    // Schemas to reset (null = all schemas)
    SchemasToInclude = ["public", "orders"],

    // Database adapter
    DbAdapter = DbAdapter.Postgres, // or DbAdapter.SqlServer, DbAdapter.MySql

    // Reset command timeout (default: 60 seconds)
    CommandTimeout = 120
});
```

**Pros**: Very fast, preserves schema
**Cons**: Requires additional package, some setup
**Use Case**: Large test suites, repeated test execution

### Strategy 3: Transaction Rollback (Fast)

Use database transactions that are never committed.

```csharp
public class TransactionRollbackTests {
    PostgreSqlContainer _container;
    NpgsqlConnection    _connection;
    NpgsqlTransaction   _transaction;
    AppDbContext        _context;

    [Before(Class)]
    public async Task StartDatabase() {
        _container = new PostgreSqlBuilder().Build();
        await _container.StartAsync();

        _connection = new NpgsqlConnection(_container.GetConnectionString());
        await _connection.OpenAsync();

        // Apply migrations once
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connection)
            .Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();
    }

    [Before(Test)]
    public async Task SetUpTest() {
        // Start transaction
        _transaction = await _connection.BeginTransactionAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connection)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.UseTransactionAsync(_transaction);
    }

    [After(Test)]
    public async Task TearDownTest() {
        // Rollback transaction (undoes all changes)
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();

        await _context.DisposeAsync();
    }

    [After(Class)]
    public async Task StopDatabase() {
        await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task changes_are_rolled_back() {
        _context.Orders.Add(new Order {
            CustomerId = "customer-123",
            OrderDate = DateTime.UtcNow,
            TotalAmount = 99.99m
        });

        await _context.SaveChangesAsync();

        var count = await _context.Orders.CountAsync();
        await Assert.That(count).IsEqualTo(1);

        // Changes will be rolled back after test
    }

    [Test]
    public async Task next_test_has_clean_state() {
        // Previous test changes were rolled back
        var count = await _context.Orders.CountAsync();
        await Assert.That(count).IsEqualTo(0);
    }
}
```

**Pros**: Very fast, automatic cleanup
**Cons**: Can't test transaction behavior, identity/sequence values not reset
**Use Case**: Tests that don't test transaction behavior

### Strategy 4: Manual Cleanup

Manually delete test data after each test.

```csharp
[After(Test)]
public async Task ManualCleanup() {
    _context.OrderItems.RemoveRange(_context.OrderItems);
    _context.Orders.RemoveRange(_context.Orders);
    _context.Customers.RemoveRange(_context.Customers);

    await _context.SaveChangesAsync();
}
```

**Pros**: Simple, no additional packages
**Cons**: Error-prone, must maintain cleanup list, slow
**Use Case**: Small test suites, simple schemas

## Test Data Management

### Test Data Builders

```csharp
public class OrderBuilder {
    Order _order = new() {
        CustomerId  = "customer-123",
        OrderDate   = DateTime.UtcNow,
        Status      = OrderStatus.Pending,
        TotalAmount = 0m
    };

    public OrderBuilder WithCustomerId(string customerId) {
        _order.CustomerId = customerId;
        return this;
    }

    public OrderBuilder WithOrderDate(DateTime orderDate) {
        _order.OrderDate = orderDate;
        return this;
    }

    public OrderBuilder WithItems(params OrderItem[] items) {
        _order.Items       = items.ToList();
        _order.TotalAmount = items.Sum(i => i.Price * i.Quantity);
        return this;
    }

    public OrderBuilder WithStatus(OrderStatus status) {
        _order.Status = status;
        return this;
    }

    public Order Build() => _order;
}

// Usage:
[Test]
public async Task test_with_builder() {
    var order = new OrderBuilder()
        .WithCustomerId("customer-456")
        .WithItems(
            new OrderItem { ProductId = "prod-1", Quantity = 2, Price = 19.99m },
            new OrderItem { ProductId = "prod-2", Quantity = 1, Price = 49.99m }
        )
        .WithStatus(OrderStatus.Pending)
        .Build();

    _context.Orders.Add(order);
    await _context.SaveChangesAsync();

    await Assert.That(order.TotalAmount).IsEqualTo(89.97m);
}
```

### Test Data Factories

```csharp
public static class TestDataFactory {
    public static Customer CreateCustomer(string id = null, string name = null, string email = null) {
        return new Customer {
            Id    = id ?? $"customer-{Guid.NewGuid()}",
            Name  = name ?? "Test Customer",
            Email = email ?? $"test-{Guid.NewGuid()}@example.com"
        };
    }

    public static Order CreateOrder(
        string customerId = null,
        DateTime? orderDate = null,
        OrderStatus status = OrderStatus.Pending,
        params OrderItem[] items
    ) {
        return new Order {
            CustomerId  = customerId ?? "customer-123",
            OrderDate   = orderDate ?? DateTime.UtcNow,
            Status      = status,
            Items       = items.ToList(),
            TotalAmount = items.Sum(i => i.Price * i.Quantity)
        };
    }

    public static OrderItem CreateOrderItem(
        string productId = null,
        int quantity = 1,
        decimal price = 9.99m
    ) {
        return new OrderItem {
            ProductId = productId ?? $"prod-{Guid.NewGuid()}",
            Quantity  = quantity,
            Price     = price
        };
    }
}

// Usage:
[Test]
public async Task test_with_factory() {
    var customer = TestDataFactory.CreateCustomer();
    var order = TestDataFactory.CreateOrder(
        customerId: customer.Id,
        items: [
            TestDataFactory.CreateOrderItem(quantity: 2, price: 19.99m),
            TestDataFactory.CreateOrderItem(quantity: 1, price: 49.99m)
        ]
    );

    _context.Customers.Add(customer);
    _context.Orders.Add(order);
    await _context.SaveChangesAsync();
}
```

### Shared Test Fixtures

```csharp
public class DatabaseTestFixture {
    public PostgreSqlContainer Container { get; private set; }
    public NpgsqlConnection    Connection { get; private set; }
    public Respawner           Respawner { get; private set; }

    public async Task InitializeAsync() {
        Container = new PostgreSqlBuilder().Build();
        await Container.StartAsync();

        Connection = new NpgsqlConnection(Container.GetConnectionString());
        await Connection.OpenAsync();

        // Apply migrations
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Connection)
            .Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        // Initialize Respawner
        Respawner = await Respawner.CreateAsync(Connection, new RespawnerOptions {
            TablesToIgnore = ["__EFMigrationsHistory"],
            DbAdapter = DbAdapter.Postgres
        });
    }

    public async Task<AppDbContext> CreateContextAsync() {
        await Respawner.ResetAsync(Connection);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Connection)
            .Options;

        return new AppDbContext(options);
    }

    public async Task DisposeAsync() {
        await Connection.DisposeAsync();
        await Container.DisposeAsync();
    }
}

// Usage:
public class OrderRepositoryTests {
    static DatabaseTestFixture _fixture;
    AppDbContext               _context;

    [Before(Class)]
    public static async Task SetUpFixture() {
        _fixture = new DatabaseTestFixture();
        await _fixture.InitializeAsync();
    }

    [Before(Test)]
    public async Task SetUpTest() {
        _context = await _fixture.CreateContextAsync();
    }

    [After(Test)]
    public async Task TearDownTest() {
        await _context.DisposeAsync();
    }

    [After(Class)]
    public static async Task TearDownFixture() {
        await _fixture.DisposeAsync();
    }
}
```

## Migration Testing

### Testing Migrations Apply Cleanly

```csharp
[Test]
public async Task all_migrations_apply_successfully() {
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_container.GetConnectionString())
        .Options;

    await using var context = new AppDbContext(options);

    // This throws if migrations fail
    await context.Database.MigrateAsync();

    var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

    await Assert.That(appliedMigrations).IsNotEmpty();
}
```

### Testing Migration Reversibility

```csharp
[Test]
public async Task migrations_can_be_rolled_back() {
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_container.GetConnectionString())
        .Options;

    await using var context = new AppDbContext(options);

    // Apply all migrations
    await context.Database.MigrateAsync();

    // Get all migrations
    var allMigrations = context.Database.GetMigrations().ToList();
    var lastMigration = allMigrations[^2]; // Second to last migration

    // Roll back to previous migration
    await context.Database.MigrateAsync(lastMigration);

    var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

    await Assert.That(appliedMigrations).DoesNotContain(allMigrations[^1]);
}
```

## Performance Testing

### Measuring Query Performance

```csharp
[Test]
public async Task complex_query_completes_within_threshold() {
    // Seed large dataset
    await SeedLargeDatasetAsync(10000);

    var stopwatch = Stopwatch.StartNew();

    var orders = await _context.Orders
        .Include(o => o.Customer)
        .Include(o => o.Items)
        .Where(o => o.OrderDate >= DateTime.UtcNow.AddDays(-30))
        .OrderByDescending(o => o.TotalAmount)
        .Take(100)
        .ToListAsync();

    stopwatch.Stop();

    await Assert.That(orders).HasCount(100);
    await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(1000); // Under 1 second
}
```

### Testing Index Effectiveness

```csharp
[Test]
public async Task query_uses_index_efficiently() {
    await SeedLargeDatasetAsync(100000);

    // Enable query logging
    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_container.GetConnectionString())
        .UseLoggerFactory(loggerFactory)
        .EnableSensitiveDataLogging()
        .Options;

    await using var context = new AppDbContext(options);

    var stopwatch = Stopwatch.StartNew();

    var orders = await context.Orders
        .Where(o => o.CustomerId == "customer-123") // Should use index
        .ToListAsync();

    stopwatch.Stop();

    // Fast enough to indicate index use
    await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(100);
}
```

## Best Practices

1. **Choose Appropriate Cleanup Strategy**:
   - Small test suite: `EnsureDeleted + Migrate`
   - Large test suite: Respawn
   - Tests not testing transactions: Transaction Rollback

2. **Use Test Data Builders**: Create readable, maintainable test data

3. **Reuse Expensive Resources**: Share containers across test classes

4. **Test Against Production Database**: Use same database engine as production

5. **Test Migrations**: Ensure migrations apply and roll back cleanly

6. **Measure Performance**: Include performance assertions for critical queries

7. **Isolate Tests**: Each test should be independent and not rely on test execution order

---

These strategies provide comprehensive coverage for database testing scenarios. Select the approach that best fits your test suite size, complexity, and performance requirements.
