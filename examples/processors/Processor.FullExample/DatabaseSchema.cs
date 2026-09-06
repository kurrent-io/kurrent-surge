namespace Processor.FullExample;

static class DatabaseSchema {
    public const string CreateClubSpendingTable = """
        CREATE TABLE IF NOT EXISTS club_spending (
            club_id        TEXT          NOT NULL,
            season         INT           NOT NULL,
            total_spent    DECIMAL(18,2) NOT NULL DEFAULT 0,
            total_received DECIMAL(18,2) NOT NULL DEFAULT 0,
            players_in     INT           NOT NULL DEFAULT 0,
            players_out    INT           NOT NULL DEFAULT 0,
            updated_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            PRIMARY KEY (club_id, season)
        );
        """;
}
