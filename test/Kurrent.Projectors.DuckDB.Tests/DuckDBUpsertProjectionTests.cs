using Bogus;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB.Tests;

using static ProjectionTestRecords;

/// <summary>
/// Covers <see cref="DuckDBUpsertProjection{TDocument}"/> — the shape that writes straight through, with
/// no load and no coalescing. Its whole claim is that replaying a batch changes nothing, which is what
/// makes it safe to pick over <see cref="DuckDBDocumentProjection{TDocument,TKey}"/>.
/// </summary>
[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(30_000)]
public class DuckDBUpsertProjectionTests {
    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Writing <<-

    [Test]
    public async ValueTask writes_the_document_as_soon_as_the_batch_is_applied() {
        // Arrange — no working set and no flush: unlike the document shape, the row is durable to the
        // open transaction by the time Apply returns.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var profile    = NewProfile();

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(Changed(profile), NextPosition())));

        // Assert
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo([profile]);
    }

    [Test]
    public async ValueTask writes_one_document_per_matching_record() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var profiles   = Enumerable.Range(0, 4).Select(_ => NewProfile()).ToArray();

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            profiles.Select(profile => Decoded(Changed(profile), NextPosition())).ToArray()));

        // Assert
        await Assert.That(projection.Upserts).IsEqualTo(profiles.Length);
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo(profiles);
    }

    [Test]
    public async ValueTask lets_the_last_message_for_a_key_win_within_one_batch() {
        // Arrange — nothing is coalesced, so both writes happen; the conflict clause is the only merge.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var first      = NewProfile();
        var last       = first with { Name = Faker.Name.FullName(), Score = first.Score + 10 };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(Changed(first), NextPosition()),
            Decoded(Changed(last), NextPosition())));

        // Assert
        await Assert.That(projection.Upserts).IsEqualTo(2);
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo([last]);
    }

    [Test]
    public async ValueTask leaves_the_same_rows_behind_when_a_batch_is_applied_twice() {
        // Arrange — this is the shape's reason to exist. A replay after a rolled-back epoch re-applies
        // records that may already be reflected in storage, and that has to be a no-op.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var profiles   = Enumerable.Range(0, 3).Select(_ => NewProfile()).ToArray();

        var batch = profiles.Select(profile => Decoded(Changed(profile), NextPosition())).ToArray();

        // Act
        await projection.OnProcessBatch(connection, BatchOf(batch));
        await projection.OnProcessBatch(connection, BatchOf(batch));

        // Assert
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo(profiles);
    }

    [Test]
    public async ValueTask runs_an_async_handler() {
        // Arrange — Project has a sync and an async overload; only the sync one is exercised elsewhere.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var profile    = NewProfile();

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new ProfileImported(profile.Id, profile.Name, profile.Score), NextPosition())));

        // Assert
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo([profile]);
    }

    [Test]
    public async ValueTask hands_the_handler_the_position_of_the_record_being_projected() {
        // Arrange — the handler stamps the record's log position into the score, so reading it back
        // proves the context carried this record's own position rather than a default.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var profile    = NewProfile();
        var position   = NextPosition();

        var expected = new[] { profile with { Score = (int)position } };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new ProfileStamped(profile.Id, profile.Name), position)));

        // Assert
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo(expected);
    }

    #endregion

    #region ->> Dispatch <<-

    [Test]
    public async ValueTask writes_nothing_for_a_record_whose_handler_returns_null() {
        // Arrange — returning null is the documented way to say "this message writes nothing". Writing a
        // default document instead would be indistinguishable downstream from a real one.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var kept       = NewProfile();

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(Changed(kept), NextPosition()),
            Decoded(new ProfileIgnored(NewProfile().Id), NextPosition())));

        // Assert
        await Assert.That(projection.Upserts).IsEqualTo(1);
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo([kept]);
    }

    [Test]
    public async ValueTask skips_a_record_whose_type_has_no_handler() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var kept       = NewProfile();

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(Changed(kept), NextPosition()),
            Decoded(new ProfileUnregistered(NewProfile().Id), NextPosition())));

        // Assert
        await Assert.That(projection.Upserts).IsEqualTo(1);
        await Assert.That(ReadProfiles(connection, projection.Table)).IsEquivalentTo([kept]);
    }

    [Test]
    public async ValueTask skips_a_record_that_was_never_decoded() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Undecoded(NextPosition())));

        // Assert
        await Assert.That(projection.Upserts).IsEqualTo(0);
        await Assert.That(CountRows(connection, projection.Table)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask writes_nothing_for_an_empty_batch() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);

        // Act
        await projection.OnProcessBatch(connection, BatchOf());

        // Assert
        await Assert.That(projection.Upserts).IsEqualTo(0);
        await Assert.That(CountRows(connection, projection.Table)).IsEqualTo(0);
    }

    #endregion

    #region ->> Failure <<-

    [Test]
    public async ValueTask surfaces_the_failure_when_a_handler_throws() {
        // Arrange — Apply owns no transaction; letting the failure out is what lets the projector roll
        // back rather than commit a partial batch.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);

        // Act / Assert
        await Assert.That(async () => await projection.OnProcessBatch(
                connection, BatchOf(Decoded(new ProfileRejected(NewProfile().Id), NextPosition()))))
            .Throws<InvalidOperationException>();
    }

    #endregion

    #region ->> Projection <<-

    /// <summary>
    /// Writes a profile per message. <c>Upserts</c> counts how often storage was written, which is what
    /// separates "wrote nothing" from "wrote the same thing twice".
    /// </summary>
    sealed class ProfileProjection : DuckDBUpsertProjection<Profile> {
        public ProfileProjection(string table) {
            Table = table;

            Project<ProfileChanged>((msg, _) => new Profile(msg.ProfileId, msg.Name, msg.Score));

            Project<ProfileImported>((msg, _) =>
                ValueTask.FromResult<Profile?>(new Profile(msg.ProfileId, msg.Name, msg.Score)));

            Project<ProfileStamped>((msg, context) =>
                new Profile(msg.ProfileId, msg.Name, (int)context.Record.Position.LogPosition.Value));

            Project<ProfileIgnored>((_, _) => null);

            // Spelled out because a throw expression fits both the sync and the async Project overload.
            Func<ProfileRejected, ProjectionContext<DuckDBAdvancedConnection>, Profile?> reject =
                (_, _) => throw new InvalidOperationException("Deliberate handler failure.");

            Project(reject);
        }

        public string Table { get; }

        public int Upserts { get; private set; }

        protected override async ValueTask Upsert(
            DuckDBAdvancedConnection connection, Profile document, CancellationToken cancellationToken
        ) {
            Upserts++;

            using var command = connection.CreateCommand();

            command.CommandText = $"""
                INSERT INTO "{Table}" (id, name, score) VALUES ($id, $name, $score)
                ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, score = EXCLUDED.score
                """;

            command.Parameters.Add(new DuckDBParameter("id", document.Id));
            command.Parameters.Add(new DuckDBParameter("name", document.Name));
            command.Parameters.Add(new DuckDBParameter("score", document.Score));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region ->> Documents and messages <<-

    public sealed record Profile(string Id, string Name, int Score);

    public sealed record ProfileChanged(string ProfileId, string Name, int Score);

    public sealed record ProfileImported(string ProfileId, string Name, int Score);

    public sealed record ProfileStamped(string ProfileId, string Name);

    public sealed record ProfileIgnored(string ProfileId);

    public sealed record ProfileRejected(string ProfileId);

    public sealed record ProfileUnregistered(string ProfileId);

    #endregion

    #region ->> Arranging <<-

    long _position;

    long NextPosition() => ++_position;

    Profile NewProfile() =>
        new($"profile-{TestId.New()}", Faker.Name.FullName(), Faker.Random.Int(1, 1_000));

    static ProfileChanged Changed(Profile profile) =>
        new(profile.Id, profile.Name, profile.Score);

    ProfileProjection NewProjection(DuckDBAdvancedConnection connection) {
        var table = $"profiles_{TestId.New()}";

        using var command = connection.CreateCommand();

        command.CommandText = $"""
            CREATE TABLE "{table}" (id TEXT PRIMARY KEY, name TEXT NOT NULL, score INTEGER NOT NULL)
            """;

        command.ExecuteNonQuery();

        return new ProfileProjection(table);
    }

    #endregion

    #region ->> Reading back <<-

    static Profile[] ReadProfiles(DuckDBAdvancedConnection connection, string table) {
        using var command = connection.CreateCommand();

        command.CommandText = $"""SELECT id, name, score FROM "{table}" """;

        using var reader = command.ExecuteReader();

        var profiles = new List<Profile>();

        while (reader.Read())
            profiles.Add(new Profile(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));

        return profiles.ToArray();
    }

    static int CountRows(DuckDBAdvancedConnection connection, string table) {
        using var command = connection.CreateCommand();

        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    #endregion
}
