using Kurrent.Client;
using Kurrent.Client.Schema;
using Processor.Leaderboard.Events;

namespace Processor.Leaderboard;

[CustomMessageCatalog]
sealed class LeaderboardMessageCatalog() : MessageCatalog(
    "leaderboard", alwaysActive: true, catalog => catalog
        .Map<ScoreSubmitted>("ScoreSubmitted")
        .Map<PlayerJoined>("PlayerJoined"));
