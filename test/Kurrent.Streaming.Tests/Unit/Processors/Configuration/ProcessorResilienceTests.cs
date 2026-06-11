using Bogus;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Streaming.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;
using Status = Grpc.Core.Status;

namespace Kurrent.Streaming.Tests.Options;

[Category("Processors")]
public class ProcessorResilienceTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask default_pipeline_retries_transient_grpc_errors() {
        // Arrange
        var message  = Faker.Lorem.Sentence();
        var options  = new ProcessorOptions();
        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        var expectedAttempts = 2;
        var attempts         = 0;

        // Act
        await pipeline.ExecuteAsync(_ => {
            if (++attempts == 1)
                throw new RpcException(new Status(StatusCode.Unavailable, message));

            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        // Assert
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask configured_defaults_override_retry_knobs() {
        // Arrange
        var message = Faker.Lorem.Sentence();
        var options = new ProcessorOptions();

        options.ConfigureResilience(r => {
            r.MaxRetryAttempts = 2;
            r.BaseDelay        = TimeSpan.FromMilliseconds(1);
            r.MaxDelay         = TimeSpan.FromMilliseconds(1);
        });

        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        // initial attempt + 2 retries, then the transient error propagates
        var expectedAttempts = 3;
        var attempts         = 0;

        // Act
        var act = async () => await pipeline.ExecuteAsync(_ => {
            attempts++;
            throw new RpcException(new Status(StatusCode.Unavailable, message));
        }, CancellationToken.None);

        // Assert
        await Assert.That(act).Throws<RpcException>();
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask extended_pipeline_preserves_default_retry() {
        // Arrange
        var message = Faker.Lorem.Sentence();
        var options = new ProcessorOptions();

        options.ConfigureResilience(r => {
            r.BaseDelay = TimeSpan.FromMilliseconds(1);
            r.MaxDelay  = TimeSpan.FromMilliseconds(1);
        });

        // User strategy handles an exception the SDK default never retries; the SDK
        // default must still handle transient gRPC errors after the extension.
        options.ExtendResilience(b => b.AddRetry(new RetryStrategyOptions {
            ShouldHandle     = new PredicateBuilder().Handle<InvalidOperationException>(),
            MaxRetryAttempts = 1,
            Delay            = TimeSpan.Zero
        }));

        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        // attempt 1: user-retried, attempt 2: SDK-retried, attempt 3: success
        var expectedAttempts = 3;
        var attempts         = 0;

        // Act
        await pipeline.ExecuteAsync(_ => {
            attempts++;
            return attempts switch {
                1 => throw new InvalidOperationException(message),
                2 => throw new RpcException(new Status(StatusCode.Unavailable, message)),
                _ => ValueTask.CompletedTask
            };
        }, CancellationToken.None);

        // Assert
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask replaced_pipeline_ignores_sdk_defaults() {
        // Arrange
        var message = Faker.Lorem.Sentence();
        var options = new ProcessorOptions();

        // Full replace with an empty pipeline: no retry strategy at all.
        options.ConfigureResilience((ResiliencePipelineBuilder _) => { });

        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        var expectedAttempts = 1;
        var attempts         = 0;

        // Act
        var act = async () => await pipeline.ExecuteAsync(_ => {
            attempts++;
            throw new RpcException(new Status(StatusCode.Unavailable, message));
        }, CancellationToken.None);

        // Assert
        await Assert.That(act).Throws<RpcException>();
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask reconnects_on_not_leader_node() {
        // Arrange
        var message = Faker.Lorem.Sentence();
        var options = new ProcessorOptions();

        options.ConfigureResilience(r => {
            r.BaseDelay = TimeSpan.FromMilliseconds(1);
            r.MaxDelay  = TimeSpan.FromMilliseconds(1);
        });

        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        // attempt 1: typed exception (connect phase), attempt 2: raw RpcException carrying
        // the server error code (streaming phase), attempt 3: success
        var expectedAttempts = 3;
        var attempts         = 0;

        // Act
        await pipeline.ExecuteAsync(_ => {
            attempts++;
            return attempts switch {
                1 => throw new NotLeaderNodeException(message),
                2 => throw RawServerError(Code.FailedPrecondition, NotLeaderNodeException.Code, message),
                _ => ValueTask.CompletedTask
            };
        }, CancellationToken.None);

        // Assert
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask reconnects_on_leader_forwarding_timeout() {
        // Arrange
        var message = Faker.Lorem.Sentence();
        var options = new ProcessorOptions();

        options.ConfigureResilience(r => {
            r.BaseDelay = TimeSpan.FromMilliseconds(1);
            r.MaxDelay  = TimeSpan.FromMilliseconds(1);
        });

        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        // attempt 1: typed exception (connect phase), attempt 2: raw RpcException carrying
        // the server error code (streaming phase), attempt 3: success
        var expectedAttempts = 3;
        var attempts         = 0;

        // Act
        await pipeline.ExecuteAsync(_ => {
            attempts++;
            return attempts switch {
                1 => throw new LeaderForwardingTimeoutException(message),
                2 => throw RawServerError(Code.DeadlineExceeded, LeaderForwardingTimeoutException.Code, message),
                _ => ValueTask.CompletedTask
            };
        }, CancellationToken.None);

        // Assert
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask resilience_context_carries_processor_logger() {
        // Arrange
        var message = Faker.Lorem.Sentence();
        var options = new ProcessorOptions();
        var logger  = new CapturingLogger();

        options.ConfigureResilience(r => {
            r.BaseDelay = TimeSpan.FromMilliseconds(1);
            r.MaxDelay  = TimeSpan.FromMilliseconds(1);
        });

        // User strategy reads the processor logger via the Context.Logger extension, exactly
        // as a consumer's OnRetry callback would.
        ILogger? userSawLogger = null;

        options.ExtendResilience(b => b.AddRetry(new RetryStrategyOptions {
            ShouldHandle     = new PredicateBuilder().Handle<InvalidOperationException>(),
            MaxRetryAttempts = 1,
            Delay            = TimeSpan.Zero,
            OnRetry = args => {
                userSawLogger = args.Context.Logger;
                return default;
            }
        }));

        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        var context = ResilienceContextPool.Shared.Get(CancellationToken.None);
        context.Properties.Set(ProcessorResilienceStrategy.LoggerKey, logger);

        var attempts = 0;

        // Act
        try {
            await pipeline.ExecuteAsync(_ => {
                attempts++;
                return attempts switch {
                    1 => throw new InvalidOperationException(message),
                    2 => throw new RpcException(new Status(StatusCode.Unavailable, message)),
                    _ => ValueTask.CompletedTask
                };
            }, context);
        } finally {
            ResilienceContextPool.Shared.Return(context);
        }

        // Assert
        var sdkLoggedReconnectWarning = logger.Entries.Any(e => e.Level == LogLevel.Warning);

        await Assert.That(userSawLogger).IsSameReferenceAs(logger);
        await Assert.That(sdkLoggedReconnectWarning).IsTrue();
    }

    [Test]
    public async ValueTask logger_extension_falls_back_to_null_logger_outside_processor() {
        // Arrange
        var message = Faker.Lorem.Sentence();
        var options = new ProcessorOptions();

        options.ConfigureResilience(r => {
            r.BaseDelay = TimeSpan.FromMilliseconds(1);
            r.MaxDelay  = TimeSpan.FromMilliseconds(1);
        });

        ILogger? userSawLogger = null;

        options.ExtendResilience(b => b.AddRetry(new RetryStrategyOptions {
            ShouldHandle     = new PredicateBuilder().Handle<InvalidOperationException>(),
            MaxRetryAttempts = 1,
            Delay            = TimeSpan.Zero,
            OnRetry = args => {
                userSawLogger = args.Context.Logger;
                return default;
            }
        }));

        var pipeline = options.BuildResiliencePipeline(NullLoggerFactory.Instance);

        var attempts = 0;

        // Act — plain token overload: no resilience context primed with a logger
        await pipeline.ExecuteAsync(_ => {
            attempts++;
            return attempts == 1
                ? throw new InvalidOperationException(message)
                : ValueTask.CompletedTask;
        }, CancellationToken.None);

        // Assert
        await Assert.That(userSawLogger).IsSameReferenceAs(NullLogger.Instance);
    }

    /// <summary>
    /// Builds a raw <see cref="RpcException"/> the way the server surfaces a domain error
    /// mid-stream: generic gRPC status code plus an <see cref="ErrorInfo"/> detail carrying
    /// the Kurrent error code as its reason.
    /// </summary>
    static RpcException RawServerError(Code statusCode, string reason, string message) =>
        new Google.Rpc.Status {
            Code    = (int)statusCode,
            Message = message,
            Details = { Any.Pack(new ErrorInfo { Reason = reason, Domain = "kurrent" }) }
        }.ToRpcException();

    sealed class CapturingLogger : ILogger {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
