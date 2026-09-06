using Kurrent.Surge.Processors;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Rpc;
using Kurrent.Surge.Errors;
using Kurrent.Surge.Processors.Models;
using KurrentDB.Protocol.V1.Streams;
using Status = Grpc.Core.Status;

namespace Kurrent.Surge.Streaming.Tests;

/// <summary>
/// End-to-end reconnection tests against a real server. Failures are injected client-side via a
/// gRPC interceptor on the drone's own client, so the resilience pipeline sees the exact wire
/// shapes the subscribe path produces — without disturbing the shared test container.
/// </summary>
[Category("Processors")]
[Category("Integration")]
[Timeout(120_000)]
public class ProcessorResilienceIntegrationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reconnects_after_transient_subscribe_failure(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(100, ct);

        var expectedRecords = data.Records.Length;
        var expectedFaults  = 2;

        var interceptor = new FaultingSubscriptionInterceptor(
            expectedFaults,
            () => new RpcException(new Status(StatusCode.Unavailable, "induced transient failure")));

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.ConnectionString, client => {
                client.LoggerFactory = Fixture.LoggerFactory;
                client.Interceptors  = [interceptor];
            });

            opts.ProcessorId                  = $"reconnect-transient-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);

            opts.ConfigureResilience(r => {
                r.BaseDelay = TimeSpan.FromMilliseconds(10);
                r.MaxDelay  = TimeSpan.FromMilliseconds(10);
            });
        });

        // Act
        await drone.RunAsync(ct);

        // Assert — every fault consumed, every record delivered exactly once, in order
        await Assert.That(interceptor.InjectedFaults).IsEqualTo(expectedFaults);
        await Assert.That(processor.Records.Length).IsEqualTo(expectedRecords);

        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].Revision).IsEqualTo(Revision.From(i));
    }

    [Test]
    public async ValueTask reconnects_after_not_leader_node_error(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(100, ct);

        var expectedRecords = data.Records.Length;
        var expectedFaults  = 1;

        // The connect phase translates this rich status into a typed NotLeaderNodeException —
        // the shape a real leader election produces.
        var interceptor = new FaultingSubscriptionInterceptor(expectedFaults, NotLeaderFault);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.ConnectionString, client => {
                client.LoggerFactory = Fixture.LoggerFactory;
                client.Interceptors  = [interceptor];
            });

            opts.ProcessorId                  = $"reconnect-not-leader-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);

            opts.ConfigureResilience(r => {
                r.BaseDelay = TimeSpan.FromMilliseconds(10);
                r.MaxDelay  = TimeSpan.FromMilliseconds(10);
            });
        });

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(interceptor.InjectedFaults).IsEqualTo(expectedFaults);
        await Assert.That(processor.Records.Length).IsEqualTo(expectedRecords);

        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].Revision).IsEqualTo(Revision.From(i));
    }

    [Test]
    public async ValueTask resumes_without_gaps_or_duplicates_after_mid_stream_failure(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(100, ct);

        var expectedRecords = data.Records.Length;
        var expectedFaults  = 1;

        // Deliver the confirmation plus a handful of records, then cut the stream — the raw
        // RpcException shape MoveNext produces. Resume must pick up exactly after the last
        // delivered record.
        var interceptor = new FaultingSubscriptionInterceptor(
            expectedFaults,
            () => new RpcException(new Status(StatusCode.Unavailable, "induced mid-stream failure")),
            faultAfterMessages: 6);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.ConnectionString, client => {
                client.LoggerFactory = Fixture.LoggerFactory;
                client.Interceptors  = [interceptor];
            });

            opts.ProcessorId                  = $"reconnect-mid-stream-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);

            opts.ConfigureResilience(r => {
                r.BaseDelay = TimeSpan.FromMilliseconds(10);
                r.MaxDelay  = TimeSpan.FromMilliseconds(10);
            });
        });

        // Act
        await drone.RunAsync(ct);

        // Assert — sequential revisions prove no gap and no duplicate across the cut
        await Assert.That(interceptor.InjectedFaults).IsEqualTo(expectedFaults);
        await Assert.That(processor.Records.Length).IsEqualTo(expectedRecords);

        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].Revision).IsEqualTo(Revision.From(i));
    }

    /// <summary>
    /// Builds the rich status a real leader election produces: FailedPrecondition carrying
    /// ErrorInfo (matched by the retry predicate) and the leader details the typed exception parses.
    /// </summary>
    static RpcException NotLeaderFault() =>
        new Google.Rpc.Status {
            Code    = (int)Code.FailedPrecondition,
            Message = "induced leader election",
            Details = {
                Any.Pack(new ErrorInfo { Reason = NotLeaderNodeException.Code, Domain = "kurrent" }),
                Any.Pack(new NotLeaderNodeErrorDetails {
                    CurrentLeader = new NotLeaderNodeErrorDetails.Types.NodeInfo {
                        Host = "localhost",
                        Port = 2113
                    }
                })
            }
        }.ToRpcException();

    /// <summary>
    /// Faults the first N subscription calls on the drone's client. Checkpoint loads and start
    /// resolution share the same V1 <c>Read</c> RPC, so only requests with subscription options
    /// are touched. With <paramref name="faultAfterMessages"/> set, the call connects normally and
    /// the response stream throws after that many messages instead (mid-stream shape).
    /// </summary>
    sealed class FaultingSubscriptionInterceptor(
        int faults,
        Func<RpcException> fault,
        int faultAfterMessages = 0
    ) : Interceptor {
        int _remaining = faults;
        int _injected;

        public int InjectedFaults => Volatile.Read(ref _injected);

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
        ) {
            if (request is not ReadReq { Options.Subscription: not null })
                return continuation(request, context);

            if (faultAfterMessages == 0) {
                if (TryClaimFault())
                    throw fault();

                return continuation(request, context);
            }

            var call = continuation(request, context);

            return new AsyncServerStreamingCall<TResponse>(
                new FaultAfterReader<TResponse>(call.ResponseStream, faultAfterMessages, FaultIfClaimed),
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        bool TryClaimFault() {
            if (Interlocked.Decrement(ref _remaining) < 0)
                return false;

            Interlocked.Increment(ref _injected);
            return true;
        }

        RpcException? FaultIfClaimed() =>
            TryClaimFault() ? fault() : null;

        sealed class FaultAfterReader<T>(
            IAsyncStreamReader<T> inner,
            int faultAfterMessages,
            Func<RpcException?> faultIfClaimed
        ) : IAsyncStreamReader<T> {
            int _seen;

            public T Current => inner.Current;

            public async Task<bool> MoveNext(CancellationToken cancellationToken) {
                if (_seen >= faultAfterMessages && faultIfClaimed() is { } ex)
                    throw ex;

                var moved = await inner.MoveNext(cancellationToken).ConfigureAwait(false);

                if (moved)
                    _seen++;

                return moved;
            }
        }
    }
}
