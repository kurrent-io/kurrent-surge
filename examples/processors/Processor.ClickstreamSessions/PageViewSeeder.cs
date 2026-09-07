using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Processor.ClickstreamSessions.Events;

namespace Processor.ClickstreamSessions;

sealed class PageViewSeeder(KurrentClient client, ILogger<PageViewSeeder> logger) : BackgroundService {
    const string UserAgent = "Mozilla/5.0 (demo)";

    static readonly string[] Users = ["user-101", "user-202", "user-303"];
    static readonly string[] Pages = ["/home", "/products", "/products/widget-pro", "/cart", "/checkout/complete"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // The processor subscribes from the latest position; give it a moment to attach.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        // Timestamps are simulated: two minutes between pages and thirty-five minutes between
        // visits, so the 30-minute gap rule closes sessions within seconds of wall-clock time.
        var clocks = Users.ToDictionary(user => user, _ => DateTimeOffset.UtcNow);
        var steps  = Users.ToDictionary(user => user, _ => 0);

        var i = 0;

        while (!stoppingToken.IsCancellationRequested) {
            var user   = Users[i % Users.Length];
            var step   = steps[user];
            var stream = $"clickstream-{user}";

            var view = new PageView(user, Pages[step], step == 0 ? "" : Pages[step - 1], UserAgent, clocks[user]);

            await client.Streams.AppendStream(stream, [AppendRecord.Create(view, r => r with { Stream = stream })], stoppingToken);

            logger.LogInformation("[seed] {User} {Page} @ {Time:HH:mm}", user, view.PageUrl, view.Timestamp);

            if (step == Pages.Length - 1) {
                steps[user]  =  0;
                clocks[user] += TimeSpan.FromMinutes(35);
            }
            else {
                steps[user]  =  step + 1;
                clocks[user] += TimeSpan.FromMinutes(2);
            }

            i++;

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
