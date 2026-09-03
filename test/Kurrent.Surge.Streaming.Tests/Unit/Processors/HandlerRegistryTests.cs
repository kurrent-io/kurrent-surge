using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming.Tests.Processors;

/// <summary>
/// Covers <see cref="HandlerRegistry{THandler}"/> directly: the routing (typed, fallback, nothing),
/// the typed-only <c>CanHandle</c> semantics, and the freeze — including the loud throw on late
/// registration and safety under concurrent first lookups.
/// </summary>
[Category("Processors")]
public class HandlerRegistryTests {
    #region ->> Resolution <<-

    [Test]
    public async ValueTask resolves_the_handler_registered_for_the_message_type() {
        // Arrange
        var registry = new HandlerRegistry<Handler>();
        var handler  = new Handler();

        registry.Add<OrderPlaced>(handler).Add<OrderShipped>(new Handler());

        // Act
        var match = registry.Resolve(Decoded(new OrderPlaced()));

        // Assert
        await Assert.That(match.Found).IsTrue();
        await Assert.That(match.IsFallback).IsFalse();
        await Assert.That(match.Handler).IsSameReferenceAs(handler);
    }

    [Test]
    public async ValueTask resolves_nothing_when_the_type_has_no_handler_and_no_fallback() {
        // Arrange
        var registry = new HandlerRegistry<Handler>();

        registry.Add<OrderPlaced>(new Handler());

        // Act
        var match = registry.Resolve(Decoded(new OrderShipped()));

        // Assert
        await Assert.That(match.Found).IsFalse();
        await Assert.That(match.IsFallback).IsFalse();
    }

    [Test]
    public async ValueTask resolves_the_fallback_for_an_unmatched_type() {
        // Arrange
        var registry = new HandlerRegistry<Handler>();
        var fallback = new Handler();

        registry.Add<OrderPlaced>(new Handler()).Fallback(fallback);

        // Act
        var match = registry.Resolve(Decoded(new OrderShipped()));

        // Assert
        await Assert.That(match.Found).IsTrue();
        await Assert.That(match.IsFallback).IsTrue();
        await Assert.That(match.Handler).IsSameReferenceAs(fallback);
    }

    [Test]
    public async ValueTask resolves_the_fallback_for_an_undecoded_record() {
        // Arrange
        var registry = new HandlerRegistry<Handler>();
        var fallback = new Handler();

        registry.Add<OrderPlaced>(new Handler()).Fallback(fallback);

        // Act
        var match = registry.Resolve(Undecoded());

        // Assert
        await Assert.That(match.Found).IsTrue();
        await Assert.That(match.IsFallback).IsTrue();
        await Assert.That(match.Handler).IsSameReferenceAs(fallback);
    }

    [Test]
    public async ValueTask resolves_nothing_for_an_undecoded_record_without_a_fallback() {
        // Arrange
        var registry = new HandlerRegistry<Handler>();

        registry.Add<OrderPlaced>(new Handler());

        // Act
        var match = registry.Resolve(Undecoded());

        // Assert
        await Assert.That(match.Found).IsFalse();
    }

    [Test]
    public async ValueTask prefers_the_typed_handler_over_the_fallback() {
        // Arrange
        var registry = new HandlerRegistry<Handler>();
        var typed    = new Handler();

        registry.Add<OrderPlaced>(typed).Fallback(new Handler());

        // Act
        var match = registry.Resolve(Decoded(new OrderPlaced()));

        // Assert
        await Assert.That(match.IsFallback).IsFalse();
        await Assert.That(match.Handler).IsSameReferenceAs(typed);
    }

    [Test]
    public async ValueTask replaces_the_handler_when_a_type_is_registered_again() {
        // Arrange
        var registry    = new HandlerRegistry<Handler>();
        var replacement = new Handler();

        registry.Add<OrderPlaced>(new Handler()).Add<OrderPlaced>(replacement);

        // Act
        var match = registry.Resolve(Decoded(new OrderPlaced()));

        // Assert
        await Assert.That(match.Handler).IsSameReferenceAs(replacement);
    }

    #endregion

    #region ->> CanHandle <<-

    [Test]
    public async ValueTask can_handle_reports_a_typed_registration() {
        // Arrange
        var registry = new HandlerRegistry<Handler>();

        registry.Add<OrderPlaced>(new Handler());

        // Act / Assert
        await Assert.That(registry.CanHandle(Decoded(new OrderPlaced()))).IsTrue();
        await Assert.That(registry.CanHandle(Decoded(new OrderShipped()))).IsFalse();
    }

    [Test]
    public async ValueTask can_handle_ignores_the_fallback() {
        // Arrange — the fallback is not a subscription; it participates in resolution only.
        var registry = new HandlerRegistry<Handler>();

        registry.Fallback(new Handler());

        // Act / Assert
        await Assert.That(registry.CanHandle(Decoded(new OrderPlaced()))).IsFalse();
        await Assert.That(registry.CanHandle(Undecoded())).IsFalse();
        await Assert.That(registry.HasFallback).IsTrue();
    }

    #endregion

    #region ->> Freezing <<-

    [Test]
    public async ValueTask throws_when_registering_after_dispatch_started() {
        // Arrange — the first lookup freezes the registry; a late registration would otherwise land
        // in a map dispatch no longer reads, silently.
        var registry = new HandlerRegistry<Handler>();

        registry.Add<OrderPlaced>(new Handler());
        registry.Resolve(Decoded(new OrderPlaced()));

        // Act / Assert
        await Assert.That(() => registry.Add<OrderShipped>(new Handler()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask resolves_safely_under_concurrent_first_lookups() {
        // Arrange — regression: without the freeze lock, one thread nulls the open map while
        // another is still freezing from it, which crashed instead of freezing twice.
        var registry = new HandlerRegistry<Handler>();
        var handler  = new Handler();

        registry.Add<OrderPlaced>(handler);

        var record = Decoded(new OrderPlaced());

        // Act
        var resolutions = await Task.WhenAll(Enumerable
            .Range(0, Environment.ProcessorCount * 4)
            .Select(_ => Task.Run(() => {
                var match = registry.Resolve(record);
                return match.Found && ReferenceEquals(match.Handler, handler);
            })));

        // Assert
        await Assert.That(resolutions).DoesNotContain(false);
    }

    #endregion

    #region ->> Records and handlers <<-

    sealed record Handler;

    sealed record OrderPlaced;

    sealed record OrderShipped;

    long _position;

    Record Decoded<T>(T message) where T : class =>
        new() {
            Id        = RecordId.New(),
            Position  = At(++_position),
            Value     = message,
            ValueType = typeof(T)
        };

    Record Undecoded() =>
        new() {
            Id       = RecordId.New(),
            Position = At(++_position)
        };

    static RecordPosition At(long logPosition) =>
        RecordPosition.From(LogPosition.From(logPosition), Partition.From(0));

    #endregion
}
