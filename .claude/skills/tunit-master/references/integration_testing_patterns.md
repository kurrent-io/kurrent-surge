# Integration Testing Patterns

Comprehensive patterns for integration testing .NET applications with TUnit, covering web APIs, databases, message queues, external services, and complex multi-component scenarios.

## Web API Integration Testing

### ASP.NET Core WebApplicationFactory Pattern

The `WebApplicationFactory<TEntryPoint>` provides an in-memory test server for full HTTP pipeline testing.

**Basic Setup:**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

public class ApiIntegrationTests {
    WebApplicationFactory<Program> _factory;
    HttpClient                     _client;

    [Before(Class)]
    public void SetUp() {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [After(Class)]
    public async Task TearDown() {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task get_health_check_returns_healthy() {
        var response = await _client.GetAsync("/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("Healthy");
    }
}
```

### Custom Web Application Factory

Override services and configuration for testing:

```csharp
public class CustomWebApplicationFactory : WebApplicationFactory<Program> {
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.ConfigureServices(services => {
            // Remove production database
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) {
                services.Remove(descriptor);
            }

            // Add in-memory database
            services.AddDbContext<AppDbContext>(options => {
                options.UseInMemoryDatabase("TestDb");
            });

            // Replace external services with test doubles
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, FakeEmailService>();

            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        });

        builder.ConfigureAppConfiguration((context, config) => {
            config.AddInMemoryCollection(new Dictionary<string, string> {
                ["ConnectionStrings:Database"] = "InMemory",
                ["Features:PaymentProcessing"] = "true",
                ["RateLimit:RequestsPerMinute"] = "1000"
            });
        });

        builder.UseEnvironment("Test");
    }
}

// Usage:
public class OrderApiTests {
    CustomWebApplicationFactory _factory;
    HttpClient                  _client;

    [Before(Class)]
    public void SetUp() {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();
    }
}
```

### Testing Authentication and Authorization

**JWT Token Authentication:**

```csharp
public class AuthenticatedApiTests {
    WebApplicationFactory<Program> _factory;
    HttpClient                     _client;
    string                         _authToken;

    [Before(Test)]
    public async Task SetUp() {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();

        // Obtain authentication token
        var loginRequest = new { Username = "testuser", Password = "TestPass123!" };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        _authToken = loginResult.Token;

        // Configure client with auth header
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _authToken);
    }

    [Test]
    public async Task authenticated_user_can_access_protected_endpoint() {
        var response = await _client.GetAsync("/api/orders");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task unauthenticated_request_returns_401() {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/orders");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
```

**Role-Based Authorization:**

```csharp
[Test]
[Arguments("admin", HttpStatusCode.OK)]
[Arguments("user", HttpStatusCode.Forbidden)]
[Arguments("guest", HttpStatusCode.Forbidden)]
public async Task endpoint_authorization_respects_roles(string role, HttpStatusCode expectedStatus) {
    // Login with specific role
    var token = await GetAuthTokenForRoleAsync(role);
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Attempt to access admin-only endpoint
    var response = await _client.DeleteAsync("/api/admin/users/123");

    await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
}

async Task<string> GetAuthTokenForRoleAsync(string role) {
    var loginRequest = new { Username = $"{role}@test.com", Password = "TestPass123!" };
    var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
    var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
    return result.Token;
}
```

### Testing API Validation

```csharp
[Test]
public async Task create_order_validates_required_fields() {
    var invalidOrder = new { CustomerId = "" }; // Missing required fields

    var response = await _client.PostAsJsonAsync("/api/orders", invalidOrder);

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

    var problemDetails = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    await Assert.That(problemDetails.Errors).ContainsKey("CustomerId");
    await Assert.That(problemDetails.Errors).ContainsKey("Items");
}

[Test]
[Arguments(-1, "Amount must be positive")]
[Arguments(0, "Amount must be positive")]
[Arguments(1000001, "Amount exceeds maximum allowed value")]
public async Task create_order_validates_amount_range(decimal amount, string expectedErrorMessage) {
    var order = new {
        CustomerId = "customer-123",
        Amount = amount,
        Items = new[] { new { ProductId = "prod-1", Quantity = 1 } }
    };

    var response = await _client.PostAsJsonAsync("/api/orders", order);

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

    var problemDetails = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    await Assert.That(problemDetails.Errors["Amount"].First()).Contains(expectedErrorMessage);
}
```

### Testing API Content Negotiation

```csharp
[Test]
[Arguments("application/json", typeof(JsonSerializer))]
[Arguments("application/xml", typeof(XmlSerializer))]
public async Task api_supports_multiple_content_types(string contentType, Type expectedSerializerType) {
    _client.DefaultRequestHeaders.Accept.Clear();
    _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(contentType));

    var response = await _client.GetAsync("/api/orders/123");

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    await Assert.That(response.Content.Headers.ContentType.MediaType).IsEqualTo(contentType);
}
```

## Database Integration Testing

### In-Memory Database Testing

**Entity Framework Core InMemory Provider:**

```csharp
public class OrderRepositoryTests {
    DbContextOptions<AppDbContext> _options;
    AppDbContext                   _context;
    OrderRepository                _repository;

    [Before(Test)]
    public void SetUp() {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(_options);
        _repository = new OrderRepository(_context);
    }

    [After(Test)]
    public void TearDown() {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task save_order_persists_all_properties() {
        // Arrange
        var order = new Order {
            CustomerId = "customer-123",
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            Items = [
                new OrderItem { ProductId = "prod-1", Quantity = 2, Price = 19.99m },
                new OrderItem { ProductId = "prod-2", Quantity = 1, Price = 49.99m }
            ]
        };

        // Act
        await _repository.SaveAsync(order);

        // Assert
        var saved = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == order.Id);

        await Assert.That(saved).IsNotNull();
        await Assert.That(saved.CustomerId).IsEqualTo(order.CustomerId);
        await Assert.That(saved.Items).HasCount(2);
    }
}
```

**Limitations of InMemory Provider:**
- No referential integrity enforcement
- No transaction support
- Limited SQL feature support
- Not suitable for testing complex queries

### Real Database Testing with Testcontainers

**PostgreSQL Integration:**

```csharp
using Testcontainers.PostgreSql;

public class OrderRepositoryIntegrationTests {
    PostgreSqlContainer _container;
    AppDbContext        _context;
    OrderRepository     _repository;

    [Before(Class)]
    public async Task SetUpDatabase() {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync(); // Apply migrations

        _repository = new OrderRepository(_context);
    }

    [After(Class)]
    public async Task TearDownDatabase() {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task complex_query_with_joins_returns_correct_results() {
        // Seed test data
        await SeedTestDataAsync();

        // Execute complex query
        var orders = await _repository.GetOrdersWithCustomerAndItemsAsync("customer-123");

        await Assert.That(orders).HasCount(3);
        await Assert.That(orders).All(o => o.Customer != null);
        await Assert.That(orders).All(o => o.Items.Count > 0);
    }

    async Task SeedTestDataAsync() {
        var customer = new Customer { Id = "customer-123", Name = "John Doe" };
        _context.Customers.Add(customer);

        for (int i = 0; i < 3; i++) {
            var order = new Order {
                CustomerId = customer.Id,
                OrderDate = DateTime.UtcNow.AddDays(-i),
                Items = [
                    new OrderItem { ProductId = $"prod-{i}", Quantity = 1, Price = 10.00m }
                ]
            };
            _context.Orders.Add(order);
        }

        await _context.SaveChangesAsync();
    }
}
```

### Database Cleanup with Respawn

Respawn resets database state between tests efficiently:

```csharp
using Respawn;
using Npgsql;

public class DatabaseResetTests {
    static NpgsqlConnection _connection;
    static Respawner        _respawner;

    OrderRepository         _repository;

    [Before(Class)]
    public static async Task InitializeDatabase() {
        _connection = new NpgsqlConnection("Host=localhost;Database=testdb;...");
        await _connection.OpenAsync();

        // Run migrations
        await ApplyMigrationsAsync(_connection);

        // Initialize Respawner
        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions {
            TablesToIgnore = ["__EFMigrationsHistory"],
            DbAdapter = DbAdapter.Postgres
        });
    }

    [Before(Test)]
    public async Task ResetDatabase() {
        await _respawner.ResetAsync(_connection);
        _repository = new OrderRepository(_connection);
    }

    [After(Class)]
    public static async Task CloseDatabase() {
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task test_1_has_clean_database() {
        var count = await _repository.GetOrderCountAsync();
        await Assert.That(count).IsEqualTo(0);

        await _repository.SaveAsync(new Order { /* ... */ });
    }

    [Test]
    public async Task test_2_also_has_clean_database() {
        // Database reset between tests
        var count = await _repository.GetOrderCountAsync();
        await Assert.That(count).IsEqualTo(0);
    }
}
```

## Message Queue Integration Testing

### RabbitMQ Testing

```csharp
using Testcontainers.RabbitMq;
using RabbitMQ.Client;

public class OrderEventPublisherTests {
    RabbitMqContainer      _container;
    IConnection            _connection;
    OrderEventPublisher    _publisher;
    List<OrderEvent>       _receivedEvents;

    [Before(Class)]
    public async Task SetUpRabbitMq() {
        _container = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .Build();

        await _container.StartAsync();

        var factory = new ConnectionFactory {
            Uri = new Uri(_container.GetConnectionString())
        };

        _connection = factory.CreateConnection();
        _publisher = new OrderEventPublisher(_connection);

        // Set up test consumer
        SetUpTestConsumer();
    }

    void SetUpTestConsumer() {
        _receivedEvents = new List<OrderEvent>();

        using var channel = _connection.CreateModel();
        channel.QueueDeclare(
            queue: "order-events",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (sender, args) => {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            var @event = JsonSerializer.Deserialize<OrderEvent>(json);
            _receivedEvents.Add(@event);
        };

        channel.BasicConsume("order-events", autoAck: true, consumer);
    }

    [After(Class)]
    public async Task TearDownRabbitMq() {
        _connection?.Dispose();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task publish_order_created_event_delivers_to_queue() {
        // Arrange
        var @event = new OrderCreatedEvent {
            OrderId = "order-123",
            CustomerId = "customer-456",
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _publisher.PublishAsync(@event);

        // Wait for message delivery
        await Task.Delay(500);

        // Assert
        await Assert.That(_receivedEvents).HasCount(1);
        await Assert.That(_receivedEvents[0].OrderId).IsEqualTo(@event.OrderId);
        await Assert.That(_receivedEvents[0].CustomerId).IsEqualTo(@event.CustomerId);
    }

    [Test]
    public async Task publish_100_events_all_delivered_successfully() {
        // Arrange
        var events = Enumerable.Range(0, 100)
            .Select(i => new OrderCreatedEvent {
                OrderId = $"order-{i}",
                CustomerId = $"customer-{i % 10}"
            })
            .ToList();

        // Act
        foreach (var @event in events) {
            await _publisher.PublishAsync(@event);
        }

        // Wait for all messages
        await Task.Delay(2000);

        // Assert
        await Assert.That(_receivedEvents).HasCount(100);
    }
}
```

### Testing Message Consumer

```csharp
public class OrderEventConsumerTests {
    RabbitMqContainer      _container;
    IConnection            _connection;
    OrderEventConsumer     _consumer;
    FakeOrderService       _fakeOrderService;

    [Before(Class)]
    public async Task SetUp() {
        _container = new RabbitMqBuilder().Build();
        await _container.StartAsync();

        _connection = new ConnectionFactory {
            Uri = new Uri(_container.GetConnectionString())
        }.CreateConnection();

        _fakeOrderService = new FakeOrderService();
        _consumer = new OrderEventConsumer(_connection, _fakeOrderService);

        await _consumer.StartAsync();
    }

    [After(Class)]
    public async Task TearDown() {
        await _consumer.StopAsync();
        _connection?.Dispose();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task consumer_processes_order_created_event() {
        // Arrange
        var @event = new OrderCreatedEvent {
            OrderId = "order-123",
            CustomerId = "customer-456"
        };

        // Act - Publish event to queue
        using var channel = _connection.CreateModel();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));
        channel.BasicPublish("", "order-events", null, body);

        // Wait for consumer to process
        await Task.Delay(1000);

        // Assert
        await Assert.That(_fakeOrderService.ProcessedOrders).HasCount(1);
        await Assert.That(_fakeOrderService.ProcessedOrders[0]).IsEqualTo(@event.OrderId);
    }
}
```

## External Service Integration Testing

### HTTP Client Testing with MockHttp

```csharp
using RichardSzalay.MockHttp;

public class ExternalApiClientTests {
    MockHttpMessageHandler _mockHttp;
    ExternalApiClient      _client;

    [Before(Test)]
    public void SetUp() {
        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("https://api.example.com");

        _client = new ExternalApiClient(httpClient);
    }

    [Test]
    public async Task get_customer_data_deserializes_response_correctly() {
        // Arrange
        var expectedResponse = new {
            CustomerId = "customer-123",
            Name = "John Doe",
            Email = "john@example.com"
        };

        _mockHttp
            .When("GET", "https://api.example.com/customers/customer-123")
            .Respond("application/json", JsonSerializer.Serialize(expectedResponse));

        // Act
        var result = await _client.GetCustomerAsync("customer-123");

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.CustomerId).IsEqualTo("customer-123");
        await Assert.That(result.Name).IsEqualTo("John Doe");
    }

    [Test]
    public async Task handles_external_api_timeout_gracefully() {
        // Arrange
        _mockHttp
            .When("GET", "*")
            .Respond(async () => {
                await Task.Delay(TimeSpan.FromSeconds(10));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        // Act & Assert
        await Assert.That(async () => await _client.GetCustomerAsync("customer-123"))
            .Throws<TimeoutException>();
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest)]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task handles_http_error_codes(HttpStatusCode statusCode) {
        // Arrange
        _mockHttp
            .When("GET", "*")
            .Respond(statusCode);

        // Act & Assert
        await Assert.That(async () => await _client.GetCustomerAsync("customer-123"))
            .Throws<HttpRequestException>();
    }
}
```

### WireMock for External Service Stubbing

```csharp
using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

public class PaymentGatewayIntegrationTests {
    WireMockServer       _mockServer;
    PaymentGatewayClient _client;

    [Before(Test)]
    public void SetUp() {
        _mockServer = WireMockServer.Start();
        _client = new PaymentGatewayClient(_mockServer.Urls[0]);
    }

    [After(Test)]
    public void TearDown() {
        _mockServer.Stop();
        _mockServer.Dispose();
    }

    [Test]
    public async Task process_payment_sends_correct_request() {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath("/payments")
                .WithHeader("Authorization", "Bearer *")
                .WithBody(new JsonMatcher(new {
                    amount = 99.99,
                    currency = "USD"
                })))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(new {
                    transactionId = "txn-123",
                    status = "approved"
                })));

        // Act
        var result = await _client.ProcessPaymentAsync(99.99m, "USD");

        // Assert
        await Assert.That(result.TransactionId).IsEqualTo("txn-123");
        await Assert.That(result.Status).IsEqualTo("approved");

        // Verify request was made
        var logEntries = _mockServer.LogEntries;
        await Assert.That(logEntries).HasCount(1);
    }
}
```

## Multi-Component Integration Testing

### Full End-to-End Integration Test

```csharp
public class EndToEndOrderProcessingTests {
    // Infrastructure
    PostgreSqlContainer            _database;
    RedisContainer                 _cache;
    RabbitMqContainer              _messageQueue;
    WireMockServer                 _paymentGatewayMock;

    // Application
    WebApplicationFactory<Program> _app;
    HttpClient                     _client;

    [Before(Class)]
    public async Task SetUpInfrastructure() {
        // Start all infrastructure in parallel
        await Task.WhenAll(
            StartDatabaseAsync(),
            StartCacheAsync(),
            StartMessageQueueAsync()
        );

        StartPaymentGatewayMock();

        // Configure application
        _app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureAppConfiguration((context, config) => {
                    config.AddInMemoryCollection(new Dictionary<string, string> {
                        ["ConnectionStrings:Database"] = _database.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = _cache.GetConnectionString(),
                        ["ConnectionStrings:RabbitMq"] = _messageQueue.GetConnectionString(),
                        ["ExternalServices:PaymentGateway"] = _paymentGatewayMock.Urls[0]
                    });
                });
            });

        _client = _app.CreateClient();

        // Wait for application to be ready
        await WaitForHealthyAsync();
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

    void StartPaymentGatewayMock() {
        _paymentGatewayMock = WireMockServer.Start();

        _paymentGatewayMock
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(new {
                    transactionId = Guid.NewGuid().ToString(),
                    status = "approved"
                })));
    }

    async Task WaitForHealthyAsync() {
        for (int i = 0; i < 30; i++) {
            try {
                var response = await _client.GetAsync("/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch { }

            await Task.Delay(1000);
        }

        throw new TimeoutException("Application did not become healthy");
    }

    [After(Class)]
    public async Task TearDownInfrastructure() {
        _client?.Dispose();
        await _app?.DisposeAsync();

        _paymentGatewayMock?.Stop();
        _paymentGatewayMock?.Dispose();

        await Task.WhenAll(
            _database.DisposeAsync().AsTask(),
            _cache.DisposeAsync().AsTask(),
            _messageQueue.DisposeAsync().AsTask()
        );
    }

    [Test]
    [Timeout(30000)]
    public async Task complete_order_flow_processes_order_caches_result_publishes_event() {
        // Arrange
        var orderRequest = new CreateOrderRequest {
            CustomerId = "customer-123",
            Items = [
                new OrderItem { ProductId = "prod-1", Quantity = 2, Price = 19.99m }
            ],
            PaymentMethod = "credit_card"
        };

        // Act - Create order
        var createResponse = await _client.PostAsJsonAsync("/api/orders", orderRequest);

        // Assert - Order created
        await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
        await Assert.That(createdOrder.OrderId).IsNotNull();

        // Assert - Order persisted to database
        var getResponse = await _client.GetAsync($"/api/orders/{createdOrder.OrderId}");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Assert - Order cached in Redis
        var redis = ConnectionMultiplexer.Connect(_cache.GetConnectionString());
        var cachedOrder = await redis.GetDatabase().StringGetAsync($"order:{createdOrder.OrderId}");
        await Assert.That(cachedOrder.HasValue).IsTrue();

        // Assert - Payment processed
        var paymentLogs = _paymentGatewayMock.LogEntries.ToList();
        await Assert.That(paymentLogs).HasCount(1);
        await Assert.That(paymentLogs[0].RequestMessage.Path).IsEqualTo("/payments");

        // Assert - Event published to RabbitMQ
        await Task.Delay(500); // Allow time for async event publishing

        var rabbitConnection = new ConnectionFactory {
            Uri = new Uri(_messageQueue.GetConnectionString())
        }.CreateConnection();

        using var channel = rabbitConnection.CreateModel();
        var message = channel.BasicGet("order-events", autoAck: true);
        await Assert.That(message).IsNotNull();

        var eventJson = Encoding.UTF8.GetString(message.Body.ToArray());
        await Assert.That(eventJson).Contains(createdOrder.OrderId);
    }
}
```

## Best Practices

### 1. Isolation

Each test should be independent:

```csharp
// ✅ GOOD: Each test has clean state
[Before(Test)]
public async Task SetUp() {
    await _respawner.ResetAsync(_connection);
    _repository = new OrderRepository(_connection);
}

// ❌ BAD: Tests share state
static List<Order> _orders = new(); // Shared between tests
```

### 2. Determinism

Tests should produce consistent results:

```csharp
// ✅ GOOD: Deterministic test data
var order = new Order {
    OrderDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
    CustomerId = "customer-123"
};

// ❌ BAD: Non-deterministic
var order = new Order {
    OrderDate = DateTime.Now, // Changes with each run
    CustomerId = Guid.NewGuid().ToString() // Random value
};
```

### 3. Meaningful Assertions

Assert on behavior, not implementation:

```csharp
// ✅ GOOD: Tests behavior
await Assert.That(order.Status).IsEqualTo(OrderStatus.Pending);
await Assert.That(order.Items).HasCount(2);

// ❌ BAD: Tests implementation details
await Assert.That(_repository.SaveAsyncWasCalled).IsTrue();
```

### 4. Test Data Builders

Use builders for complex test data:

```csharp
public class OrderBuilder {
    Order _order = new() {
        CustomerId = "customer-123",
        OrderDate = DateTime.UtcNow,
        Status = OrderStatus.Pending
    };

    public OrderBuilder WithCustomerId(string customerId) {
        _order.CustomerId = customerId;
        return this;
    }

    public OrderBuilder WithItems(params OrderItem[] items) {
        _order.Items = items.ToList();
        return this;
    }

    public Order Build() => _order;
}

// Usage:
var order = new OrderBuilder()
    .WithCustomerId("customer-456")
    .WithItems(
        new OrderItem { ProductId = "prod-1", Quantity = 2 },
        new OrderItem { ProductId = "prod-2", Quantity = 1 }
    )
    .Build();
```

### 5. Infrastructure Lifecycle Management

Reuse expensive resources:

```csharp
// ✅ GOOD: Container shared across tests
[Before(Class)]
public static async Task StartContainer() {
    _container = new PostgreSqlBuilder().Build();
    await _container.StartAsync();
}

[Before(Test)]
public async Task ResetDatabase() {
    await _respawner.ResetAsync(_connection); // Fast reset
}

[After(Class)]
public static async Task StopContainer() {
    await _container.DisposeAsync();
}

// ❌ BAD: Container recreated per test (slow)
[Before(Test)]
public async Task StartContainer() {
    _container = new PostgreSqlBuilder().Build();
    await _container.StartAsync();
}

[After(Test)]
public async Task StopContainer() {
    await _container.DisposeAsync();
}
```

---

These patterns provide comprehensive coverage for integration testing scenarios. Adapt them to your specific application architecture and testing requirements.
