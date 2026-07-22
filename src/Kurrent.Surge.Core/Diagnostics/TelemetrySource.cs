using System.Diagnostics;

namespace Kurrent.Surge.Streaming.Diagnostics;

/// <summary>
/// Abstract base for dual-dispatch diagnostics loggers.
/// Routes signals to <see cref="ILogger"/> (structured logging) and <see cref="DiagnosticSource"/> (metrics/observers).
/// </summary>
/// <remarks>
/// <para>
/// One call site dispatches to two independent gates:
/// <list type="bullet">
///   <item><see cref="ILogger"/> — structured logs via <c>[LoggerMessage]</c> source-generated partial methods.</item>
///   <item><see cref="DiagnosticSource"/> — typed event payloads for metrics observers.</item>
/// </list>
/// Neither gate allocates if nobody is listening.
/// </para>
/// <para>
/// The <see cref="Logger"/> property is consumed by the <c>[LoggerMessage]</c> source generator.
/// Derived classes declare <c>partial void LogXxx(...)</c> methods decorated with
/// <c>[LoggerMessage]</c>; the generator emits implementations that call <see cref="Logger"/>.
/// </para>
/// </remarks>
abstract class TelemetrySource(DiagnosticSource diagnosticSource, ActivitySource activitySource, ILogger logger) {
    protected ILogger          Logger           { get; } = logger;
    protected DiagnosticSource DiagnosticSource { get; } = diagnosticSource;
    protected ActivitySource   ActivitySource   { get; } = activitySource;

    /// <summary>
    /// Checks whether any observer is listening for the given event name on the DiagnosticSource.
    /// </summary>
    protected bool IsEnabled(string eventName) =>
        DiagnosticSource.IsEnabled(eventName);

    /// <summary>
    /// Publishes a diagnostic event.
    /// </summary>
    protected void Publish<T>(string eventName, T eventData) where T : notnull {
        if (DiagnosticSource.IsEnabled(eventName))
            DiagnosticSource.Write(eventName, eventData);
    }

    /// <summary>
    /// Publishes a diagnostic event witht he name generated from the type of the message.
    /// <br />Example: <c>GameOverEvent</c> becomes <c>GameOver</c>
    /// </summary>
    public void Publish<T>(T message) where T : notnull {
        var eventName = typeof(T).Name;

        if (eventName.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
            eventName = eventName[..^"Event".Length];

        Publish(eventName, message);
    }
}
