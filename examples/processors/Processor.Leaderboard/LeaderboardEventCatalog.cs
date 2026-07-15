using Kurrent.Surge.Schema.Catalogs;
using Processor.Leaderboard.Events;

namespace Processor.Leaderboard;

[CustomMessageCatalog]
sealed class LeaderboardEventCatalog() : MessageCatalog(
    "leaderboard", alwaysActive: true, catalog => catalog
        .Map<ScoreSubmitted>("ScoreSubmitted")
        .Map<PlayerJoined>("PlayerJoined"));
