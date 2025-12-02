# Docker Testing Guide

Comprehensive guide to integration testing with Docker containers using Testcontainers library in .NET with TUnit.

## Testcontainers Overview

Testcontainers is a library that provides lightweight, throwaway instances of Docker containers for testing. It supports databases, message queues, web servers, and custom containers.

**Benefits:**
- Real infrastructure in tests (not mocks/stubs)
- Consistent test environments
- Isolated test execution
- Easy CI/CD integration

**Installation:**

```bash
dotnet add package Testcontainers
dotnet add package Testcontainers.PostgreSql
dotnet add package Testcontainers.Redis
dotnet add package Testcontainers.RabbitMq
dotnet add package Testcontainers.MsSql
dotnet add package Testcontainers.MySql
dotnet add package Testcontainers.MongoDb
```

## Database Containers

### PostgreSQL

```csharp
using Testcontainers.PostgreSql;
using Npgsql;

public class PostgreSqlIntegrationTests {
    PostgreSqlContainer _container;
    NpgsqlConnection    _connection;

    [Before(Class)]
    public async Task StartPostgreSql() {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .WithPortBinding(5432, true) // Random host port
            .Build();

        await _container.StartAsync();

        _connection = new NpgsqlConnection(_container.GetConnectionString());
        await _connection.OpenAsync();

        // Run migrations or schema setup
        await InitializeDatabaseSchemaAsync();
    }

    [After(Class)]
    public async Task StopPostgreSql() {
        await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    async Task InitializeDatabaseSchemaAsync() {
        using var command = _connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE orders (
                id SERIAL PRIMARY KEY,
                customer_id VARCHAR(100) NOT NULL,
                order_date TIMESTAMP NOT NULL,
                total_amount DECIMAL(10, 2) NOT NULL
            );";

        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task insert_order_succeeds() {
        using var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO orders (customer_id, order_date, total_amount)
            VALUES (@customerId, @orderDate, @totalAmount)
            RETURNING id";

        command.Parameters.AddWithValue("customerId", "customer-123");
        command.Parameters.AddWithValue("orderDate", DateTime.UtcNow);
        command.Parameters.AddWithValue("totalAmount", 99.99m);

        var orderId = await command.ExecuteScalarAsync();

        await Assert.That(orderId).IsNotNull();
    }
}
```

**With Entity Framework Core:**

```csharp
public class EfCorePostgreSqlTests {
    PostgreSqlContainer _container;
    AppDbContext        _context;

    [Before(Class)]
    public async Task StartPostgreSql() {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .Build();

        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();
    }

    [After(Class)]
    public async Task StopPostgreSql() {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task entity_framework_operations_work() {
        var order = new Order {
            CustomerId = "customer-123",
            OrderDate = DateTime.UtcNow,
            TotalAmount = 99.99m
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Orders.FindAsync(order.Id);

        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved.CustomerId).IsEqualTo("customer-123");
    }
}
```

### SQL Server

```csharp
using Testcontainers.MsSql;

public class SqlServerIntegrationTests {
    MsSqlContainer _container;

    [Before(Class)]
    public async Task StartSqlServer() {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong!Passw0rd")
            .Build();

        await _container.StartAsync();
    }

    [After(Class)]
    public async Task StopSqlServer() {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task sql_server_connection_works() {
        using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@VERSION";

        var version = await command.ExecuteScalarAsync();

        await Assert.That(version).IsNotNull();
    }
}
```

### MySQL

```csharp
using Testcontainers.MySql;

public class MySqlIntegrationTests {
    MySqlContainer _container;

    [Before(Class)]
    public async Task StartMySql() {
        _container = new MySqlBuilder()
            .WithImage("mysql:8.0")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        await _container.StartAsync();
    }

    [After(Class)]
    public async Task StopMySql() {
        await _container.DisposeAsync();
    }
}
```

### MongoDB

```csharp
using Testcontainers.MongoDb;
using MongoDB.Driver;

public class MongoDbIntegrationTests {
    MongoDbContainer _container;
    IMongoClient     _client;
    IMongoDatabase   _database;

    [Before(Class)]
    public async Task StartMongoDb() {
        _container = new MongoDbBuilder()
            .WithImage("mongo:7")
            .Build();

        await _container.StartAsync();

        _client = new MongoClient(_container.GetConnectionString());
        _database = _client.GetDatabase("testdb");
    }

    [After(Class)]
    public async Task StopMongoDb() {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task insert_document_succeeds() {
        var collection = _database.GetCollection<Order>("orders");

        var order = new Order {
            CustomerId = "customer-123",
            OrderDate = DateTime.UtcNow,
            TotalAmount = 99.99m
        };

        await collection.InsertOneAsync(order);

        var retrieved = await collection.Find(o => o.Id == order.Id).FirstOrDefaultAsync();

        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved.CustomerId).IsEqualTo("customer-123");
    }
}
```

## Cache and Key-Value Stores

### Redis

```csharp
using Testcontainers.Redis;
using StackExchange.Redis;

public class RedisIntegrationTests {
    RedisContainer         _container;
    IConnectionMultiplexer _redis;

    [Before(Class)]
    public async Task StartRedis() {
        _container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _container.StartAsync();

        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    [After(Class)]
    public async Task StopRedis() {
        _redis?.Dispose();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task set_and_get_string_value() {
        var db = _redis.GetDatabase();

        await db.StringSetAsync("test-key", "test-value");
        var value = await db.StringGetAsync("test-key");

        await Assert.That(value.HasValue).IsTrue();
        await Assert.That(value.ToString()).IsEqualTo("test-value");
    }

    [Test]
    public async Task cache_with_expiration() {
        var db = _redis.GetDatabase();

        await db.StringSetAsync("expiring-key", "value", expiry: TimeSpan.FromSeconds(1));

        var valueBefore = await db.StringGetAsync("expiring-key");
        await Assert.That(valueBefore.HasValue).IsTrue();

        await Task.Delay(1100); // Wait for expiration

        var valueAfter = await db.StringGetAsync("expiring-key");
        await Assert.That(valueAfter.HasValue).IsFalse();
    }
}
```

## Message Queues

### RabbitMQ

```csharp
using Testcontainers.RabbitMq;
using RabbitMQ.Client;

public class RabbitMqIntegrationTests {
    RabbitMqContainer _container;
    IConnection       _connection;

    [Before(Class)]
    public async Task StartRabbitMq() {
        _container = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();

        await _container.StartAsync();

        var factory = new ConnectionFactory {
            Uri = new Uri(_container.GetConnectionString())
        };

        _connection = factory.CreateConnection();
    }

    [After(Class)]
    public async Task StopRabbitMq() {
        _connection?.Dispose();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task publish_and_consume_message() {
        using var channel = _connection.CreateModel();

        // Declare queue
        channel.QueueDeclare(
            queue: "test-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        // Publish message
        var message = "Hello, RabbitMQ!";
        var body = Encoding.UTF8.GetBytes(message);
        channel.BasicPublish("", "test-queue", null, body);

        // Consume message
        var result = channel.BasicGet("test-queue", autoAck: true);

        await Assert.That(result).IsNotNull();

        var receivedMessage = Encoding.UTF8.GetString(result.Body.ToArray());
        await Assert.That(receivedMessage).IsEqualTo(message);
    }
}
```

### Kafka

```csharp
using Testcontainers.Kafka;
using Confluent.Kafka;

public class KafkaIntegrationTests {
    KafkaContainer _container;

    [Before(Class)]
    public async Task StartKafka() {
        _container = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.5.0")
            .Build();

        await _container.StartAsync();
    }

    [After(Class)]
    public async Task StopKafka() {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task produce_and_consume_message() {
        var bootstrapServers = _container.GetBootstrapAddress();

        // Produce message
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig {
            BootstrapServers = bootstrapServers
        }).Build();

        await producer.ProduceAsync("test-topic", new Message<string, string> {
            Key = "test-key",
            Value = "test-message"
        });

        producer.Flush(TimeSpan.FromSeconds(10));

        // Consume message
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig {
            BootstrapServers = bootstrapServers,
            GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();

        consumer.Subscribe("test-topic");

        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(10));

        await Assert.That(consumeResult).IsNotNull();
        await Assert.That(consumeResult.Message.Value).IsEqualTo("test-message");
    }
}
```

## Cloud Service Emulators

### Azure Blob Storage (Azurite)

```csharp
using DotNet.Testcontainers.Builders;
using Azure.Storage.Blobs;

public class AzureBlobStorageTests {
    IContainer        _container;
    BlobServiceClient _blobClient;

    [Before(Class)]
    public async Task StartAzurite() {
        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite")
            .WithPortBinding(10000, 10000) // Blob service
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(10000))
            .Build();

        await _container.StartAsync();

        var connectionString =
            "DefaultEndpointsProtocol=http;" +
            "AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            $"BlobEndpoint=http://localhost:10000/devstoreaccount1;";

        _blobClient = new BlobServiceClient(connectionString);
    }

    [After(Class)]
    public async Task StopAzurite() {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task upload_and_download_blob() {
        // Create container
        var containerClient = _blobClient.GetBlobContainerClient("test-container");
        await containerClient.CreateIfNotExistsAsync();

        // Upload blob
        var blobClient = containerClient.GetBlobClient("test-blob.txt");
        var content = "Hello, Azure Blob Storage!"u8.ToArray();
        await blobClient.UploadAsync(new BinaryData(content), overwrite: true);

        // Download blob
        var downloadResult = await blobClient.DownloadContentAsync();
        var downloadedContent = downloadResult.Value.Content.ToArray();

        await Assert.That(downloadedContent).IsEqualTo(content);
    }
}
```

### LocalStack (AWS Services)

```csharp
using DotNet.Testcontainers.Builders;
using Amazon.S3;
using Amazon.S3.Model;

public class LocalStackS3Tests {
    IContainer    _container;
    IAmazonS3     _s3Client;

    [Before(Class)]
    public async Task StartLocalStack() {
        _container = new ContainerBuilder()
            .WithImage("localstack/localstack:latest")
            .WithPortBinding(4566, 4566)
            .WithEnvironment("SERVICES", "s3")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4566))
            .Build();

        await _container.StartAsync();

        _s3Client = new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            new AmazonS3Config {
                ServiceURL = "http://localhost:4566",
                ForcePathStyle = true
            }
        );
    }

    [After(Class)]
    public async Task StopLocalStack() {
        _s3Client?.Dispose();
        await _container.DisposeAsync();
    }

    [Test]
    public async Task create_bucket_and_put_object() {
        // Create bucket
        await _s3Client.PutBucketAsync("test-bucket");

        // Put object
        await _s3Client.PutObjectAsync(new PutObjectRequest {
            BucketName = "test-bucket",
            Key = "test-object.txt",
            ContentBody = "Hello, S3!"
        });

        // Get object
        var getResponse = await _s3Client.GetObjectAsync("test-bucket", "test-object.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();

        await Assert.That(content).IsEqualTo("Hello, S3!");
    }
}
```

## Custom Containers

### Generic Container Builder

For services without dedicated builders:

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

public class CustomServiceTests {
    IContainer _container;

    [Before(Class)]
    public async Task StartCustomService() {
        _container = new ContainerBuilder()
            .WithImage("custom-service:latest")
            .WithPortBinding(8080, true) // Random host port
            .WithEnvironment("CONFIG_VALUE", "test-value")
            .WithVolumeMount("/host/path", "/container/path")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPath("/health")))
            .Build();

        await _container.StartAsync();
    }

    [After(Class)]
    public async Task StopCustomService() {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task custom_service_responds_to_requests() {
        var port = _container.GetMappedPublicPort(8080);
        var baseUrl = $"http://localhost:{port}";

        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var response = await client.GetAsync("/api/data");

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }
}
```

### Container with Health Check

```csharp
[Before(Class)]
public async Task StartWithHealthCheck() {
    _container = new ContainerBuilder()
        .WithImage("postgres:16")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilCommandIsCompleted("pg_isready", "-U", "postgres"))
        .Build();

    await _container.StartAsync();
}
```

### Container with Custom Wait Strategy

```csharp
[Before(Class)]
public async Task StartWithCustomWait() {
    _container = new ContainerBuilder()
        .WithImage("rabbitmq:3-management")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilPortIsAvailable(5672)
            .UntilPortIsAvailable(15672))
        .Build();

    await _container.StartAsync();
}
```

## Multi-Container Scenarios

### Docker Compose with Testcontainers

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

public class DockerComposeTests {
    DockerComposeEnvironment _environment;

    [Before(Class)]
    public async Task StartDockerCompose() {
        _environment = new DockerComposeBuilder()
            .WithDockerComposeFile("docker-compose.test.yml")
            .WithCleanUp(true)
            .WithWaitStrategy("postgres", Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .WithWaitStrategy("redis", Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();

        await _environment.UpAsync();
    }

    [After(Class)]
    public async Task StopDockerCompose() {
        await _environment.DownAsync();
        await _environment.DisposeAsync();
    }

    [Test]
    public async Task services_are_running() {
        // Access services via localhost and exposed ports
        var postgresPort = _environment.GetContainer("postgres").GetMappedPublicPort(5432);
        var redisPort = _environment.GetContainer("redis").GetMappedPublicPort(6379);

        // Test connectivity
        using var postgresConnection = new NpgsqlConnection($"Host=localhost;Port={postgresPort};...");
        await postgresConnection.OpenAsync();

        await Assert.That(postgresConnection.State).IsEqualTo(ConnectionState.Open);
    }
}
```

**docker-compose.test.yml:**

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: testuser
      POSTGRES_PASSWORD: testpass
      POSTGRES_DB: testdb
    ports:
      - "5432"

  redis:
    image: redis:7-alpine
    ports:
      - "6379"

  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672"
      - "15672"
```

### Parallel Container Startup

Start multiple containers in parallel for faster test setup:

```csharp
[Before(Class)]
public async Task StartContainersInParallel() {
    var postgresTask = StartPostgresAsync();
    var redisTask = StartRedisAsync();
    var rabbitMqTask = StartRabbitMqAsync();

    await Task.WhenAll(postgresTask, redisTask, rabbitMqTask);
}

async Task StartPostgresAsync() {
    _postgresContainer = new PostgreSqlBuilder().Build();
    await _postgresContainer.StartAsync();
}

async Task StartRedisAsync() {
    _redisContainer = new RedisBuilder().Build();
    await _redisContainer.StartAsync();
}

async Task StartRabbitMqAsync() {
    _rabbitMqContainer = new RabbitMqBuilder().Build();
    await _rabbitMqContainer.StartAsync();
}
```

## Performance Optimization

### Container Reuse

Reuse containers across test classes:

```csharp
public static class TestInfrastructure {
    static PostgreSqlContainer _postgresContainer;
    static readonly SemaphoreSlim _initLock = new(1, 1);

    public static async Task<PostgreSqlContainer> GetPostgresContainerAsync() {
        await _initLock.WaitAsync();
        try {
            if (_postgresContainer == null) {
                _postgresContainer = new PostgreSqlBuilder().Build();
                await _postgresContainer.StartAsync();
            }

            return _postgresContainer;
        }
        finally {
            _initLock.Release();
        }
    }
}

// Usage in test classes:
[Before(Class)]
public async Task SetUp() {
    _container = await TestInfrastructure.GetPostgresContainerAsync();
}
```

### Image Caching

Pre-pull images to avoid delays:

```bash
# In CI/CD pipeline before running tests
docker pull postgres:16
docker pull redis:7-alpine
docker pull rabbitmq:3-management
```

### Cleanup Strategy

```csharp
[After(Class)]
public async Task Cleanup() {
    // Dispose in reverse order of creation
    await _app?.DisposeAsync();
    _rabbitConnection?.Dispose();
    await _rabbitMqContainer?.DisposeAsync();
    await _redisContainer?.DisposeAsync();
    await _postgresContainer?.DisposeAsync();
}
```

## Debugging Containers

### View Container Logs

```csharp
[After(Test)]
public async Task LogContainerOutput() {
    var (stdout, stderr) = await _container.GetLogsAsync();

    Console.WriteLine("Container STDOUT:");
    Console.WriteLine(stdout);

    if (!string.IsNullOrEmpty(stderr)) {
        Console.WriteLine("Container STDERR:");
        Console.WriteLine(stderr);
    }
}
```

### Execute Commands in Running Container

```csharp
[Test]
public async Task execute_command_in_container() {
    var execResult = await _container.ExecAsync(new[] { "ls", "-la", "/var/lib/postgresql/data" });

    Console.WriteLine($"Exit Code: {execResult.ExitCode}");
    Console.WriteLine($"Output: {execResult.Stdout}");

    await Assert.That(execResult.ExitCode).IsEqualTo(0);
}
```

## Best Practices

1. **Use Specific Image Tags**: Avoid `latest` tags for reproducibility
   ```csharp
   .WithImage("postgres:16") // ✅ GOOD
   .WithImage("postgres:latest") // ❌ BAD
   ```

2. **Share Containers When Possible**: Reuse expensive containers across tests
   ```csharp
   [Before(Class)] // ✅ GOOD - Container shared across tests
   public async Task StartContainer() { }
   ```

3. **Clean Up Resources**: Always dispose containers in `[After]` hooks
   ```csharp
   [After(Class)]
   public async Task Cleanup() {
       await _container?.DisposeAsync();
   }
   ```

4. **Use Wait Strategies**: Ensure containers are ready before tests run
   ```csharp
   .WithWaitStrategy(Wait.ForUnixContainer()
       .UntilPortIsAvailable(5432))
   ```

5. **Parallel Startup**: Start independent containers in parallel
   ```csharp
   await Task.WhenAll(StartDb(), StartCache(), StartQueue());
   ```

---

This guide covers comprehensive Docker container testing scenarios. Adapt these patterns to your specific infrastructure and testing needs.
