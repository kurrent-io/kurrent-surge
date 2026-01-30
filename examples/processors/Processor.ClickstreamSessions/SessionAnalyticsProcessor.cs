using System.Collections.Concurrent;
using Kurrent.Processors;
using Microsoft.Extensions.Logging;
using Processor.ClickstreamSessions.Events;

namespace Processor.ClickstreamSessions;

/// <summary>
/// Clickstream session analytics processor with gap-based session detection.
/// Groups page views into sessions based on 30-minute inactivity timeout.
/// Demonstrates session windows and gap detection patterns.
/// </summary>
public sealed class SessionAnalyticsProcessor(ILogger<SessionAnalyticsProcessor> logger) : IRecordProcessor {
    static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

    readonly ConcurrentDictionary<string, UserSession> _activeSessions = new();
    long _totalPageViews;
    long _sessionsCompleted;
    long _totalSessionDuration;

    public ValueTask OnInitialize(InitializationContext context) {
        logger.LogInformation("Initializing SessionAnalyticsProcessor: {ProcessorId}", context.Settings.ProcessorId);
        logger.LogInformation("Session timeout: {Timeout}", SessionTimeout);
        return ValueTask.CompletedTask;
    }

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            if (record.Value is PageView pageView) {
                ProcessPageView(pageView);
            }
        }

        return ValueTask.CompletedTask;
    }

    void ProcessPageView(PageView pageView) {
        _totalPageViews++;

        var existingSession = _activeSessions.GetValueOrDefault(pageView.UserId);

        if (existingSession != null) {
            var timeSinceLastActivity = pageView.Timestamp - existingSession.LastActivity;

            if (timeSinceLastActivity > SessionTimeout) {
                // Session has expired - close it and start a new one
                CloseSession(existingSession, "timeout");
                StartNewSession(pageView);
            } else {
                // Continue existing session
                existingSession.AddPageView(pageView);
                logger.LogDebug(
                    "Session {SessionId} | Page #{PageNum}: {Url}",
                    existingSession.SessionId,
                    existingSession.PageViews.Count,
                    pageView.PageUrl
                );
            }
        } else {
            // New user - start a new session
            StartNewSession(pageView);
        }
    }

    void StartNewSession(PageView pageView) {
        var session = new UserSession(
            sessionId: $"{pageView.UserId}-{pageView.Timestamp:yyyyMMddHHmmss}",
            userId: pageView.UserId,
            startTime: pageView.Timestamp
        );
        session.AddPageView(pageView);

        _activeSessions[pageView.UserId] = session;

        logger.LogInformation(
            "📊 NEW SESSION | User: {UserId} | Session: {SessionId} | Entry: {EntryPage}",
            pageView.UserId,
            session.SessionId,
            pageView.PageUrl
        );
    }

    void CloseSession(UserSession session, string reason) {
        _sessionsCompleted++;
        var duration = (int)(session.LastActivity - session.StartTime).TotalSeconds;
        _totalSessionDuration += duration;

        logger.LogInformation(
            "✅ SESSION CLOSED | Session: {SessionId} | Reason: {Reason} | Duration: {Duration}s | Pages: {Pages} | Entry: {Entry} | Exit: {Exit}",
            session.SessionId,
            reason,
            duration,
            session.PageViews.Count,
            session.EntryPage,
            session.ExitPage
        );

        _activeSessions.TryRemove(session.UserId, out _);
    }

    public ValueTask OnCheckpointComplete(CheckpointCompleteContext context) {
        // Check for and close expired sessions
        var now = DateTimeOffset.UtcNow;
        var expiredSessions = _activeSessions.Values
            .Where(s => now - s.LastActivity > SessionTimeout)
            .ToList();

        foreach (var session in expiredSessions) {
            CloseSession(session, "checkpoint-cleanup");
        }

        var avgDuration = _sessionsCompleted > 0 ? _totalSessionDuration / _sessionsCompleted : 0;

        logger.LogInformation(
            "Checkpoint #{CheckpointNumber} | Views: {Views} | Active: {Active} | Completed: {Completed} | Avg Duration: {Avg}s",
            context.Progress.Checkpoint.TotalCheckpoints,
            _totalPageViews,
            _activeSessions.Count,
            _sessionsCompleted,
            avgDuration
        );

        return ValueTask.CompletedTask;
    }

    public ValueTask OnShutdown(ShutdownContext context) {
        // Close all remaining sessions on shutdown
        foreach (var session in _activeSessions.Values.ToList()) {
            CloseSession(session, "shutdown");
        }

        logger.LogInformation(
            "Shutting down ({Reason}) | Total page views: {Views}, Sessions completed: {Sessions}",
            context.Reason,
            _totalPageViews,
            _sessionsCompleted
        );
        return ValueTask.CompletedTask;
    }

    sealed class UserSession(string sessionId, string userId, DateTimeOffset startTime) {
        public string SessionId { get; } = sessionId;
        public string UserId { get; } = userId;
        public DateTimeOffset StartTime { get; } = startTime;
        public DateTimeOffset LastActivity { get; private set; } = startTime;
        public List<PageView> PageViews { get; } = [];

        public string EntryPage => PageViews.Count > 0 ? PageViews[0].PageUrl : "";
        public string ExitPage => PageViews.Count > 0 ? PageViews[^1].PageUrl : "";

        public void AddPageView(PageView pageView) {
            PageViews.Add(pageView);
            LastActivity = pageView.Timestamp;
        }
    }
}
