# Processor.ClickstreamSessions

Clickstream analytics with gap-based session detection - a common web analytics pattern.

## What You'll Learn

- Session windows with configurable timeout (gap-based sessions)
- Tracking session state per user
- Entry/exit page analysis
- Session lifecycle management

## How It Works

1. **Input**: `PageView` events from web tracking
2. **Processing**:
   - Group page views by user
   - Detect session boundaries (30-minute inactivity gap)
   - Track session duration, page count, entry/exit pages
3. **Output**: Log session summaries when sessions close

```
PageView(user1, /home, 10:00)     → 📊 NEW SESSION
PageView(user1, /products, 10:05) → Page #2
PageView(user1, /cart, 10:10)     → Page #3
[30 minutes of inactivity]
PageView(user1, /home, 10:45)     → ✅ SESSION CLOSED (timeout)
                                  → 📊 NEW SESSION
```

## Prerequisites

- KurrentDB running on `localhost:2113`

## Running the Example

```bash
cd examples/processors/Processor.ClickstreamSessions
dotnet run
```

A hosted `PageViewSeeder` writes page views with simulated timestamps: two minutes between pages
and thirty-five minutes between visits, so sessions close within seconds of wall-clock time.
Press `Ctrl+C` to stop.

## Key Code

```csharp
void ProcessPageView(PageView pageView) {
    var existingSession = _activeSessions.GetValueOrDefault(pageView.UserId);

    if (existingSession != null) {
        var timeSinceLastActivity = pageView.Timestamp - existingSession.LastActivity;

        if (timeSinceLastActivity > SessionTimeout) {
            // Session expired - close and start new
            CloseSession(existingSession, "timeout");
            StartNewSession(pageView);
        } else {
            // Continue existing session
            existingSession.AddPageView(pageView);
        }
    } else {
        StartNewSession(pageView);
    }
}
```

## Session Metrics

| Metric | Description |
|--------|-------------|
| Duration | Time from first to last page view |
| Page Count | Number of pages viewed |
| Entry Page | First page of the session |
| Exit Page | Last page of the session |

## Sample Output

```
[INF] Session timeout: 00:30:00
[INF] 📊 NEW SESSION | User: user-123 | Session: user-123-20240115100000 | Entry: /home
[INF] ✅ SESSION CLOSED | Session: user-123-20240115100000 | Reason: timeout | Duration: 847s | Pages: 12 | Entry: /home | Exit: /checkout/complete
[INF] Checkpoint #3 | Views: 1523 | Active: 45 | Completed: 89 | Avg Duration: 412s
```

## Original Tutorials

- [AWS Clickstream Analytics](https://aws.amazon.com/blogs/big-data/create-real-time-clickstream-sessions-and-run-analytics-with-amazon-kinesis-data-analytics-aws-glue-and-amazon-athena/)
- [Flink Session Windows](https://nightlies.apache.org/flink/flink-docs-stable/docs/dev/datastream/operators/windows/#session-windows)
