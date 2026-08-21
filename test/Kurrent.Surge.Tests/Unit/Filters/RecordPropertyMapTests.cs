using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Filters;

[Category("Filters")]
public class RecordPropertyMapTests {
    // The registered alias set is the contract the expression language exposes as columns.
    // Count is asserted alongside the list so a new registration cannot land undeclared.
    static readonly string[] RegisteredAliases = [
        "record_id",
        "log_position", "position", "pos",
        "category",
        "stream",
        "stream_revision", "revision",
        "schema_name", "schema",
        "schema_id",
        "schema_format",
        "created_at", "timestamp", "ts",
        "data",
        "metadata", "properties", "props"
    ];

    [Test]
    public async Task registers_every_documented_alias() {
        // Arrange
        var map = RecordPropertyMap.Default;

        // Act & Assert
        foreach (var alias in RegisteredAliases)
            await Assert.That(map.HasProperty(alias)).IsTrue();
    }

    [Test]
    public async Task registers_no_alias_beyond_the_documented_set() {
        // Arrange
        var map           = RecordPropertyMap.Default;
        var expectedCount = RegisteredAliases.Length;

        // Act
        var actualCount = map.Count;

        // Assert
        await Assert.That(actualCount).IsEqualTo(expectedCount);
    }

    [Test]
    public async Task resolves_aliases_case_insensitively() {
        // Arrange — column identifiers are case-insensitive; column values are not
        var map = RecordPropertyMap.Default;

        // Act & Assert
        await Assert.That(map.HasProperty("STREAM")).IsTrue();
        await Assert.That(map.HasProperty("Schema_Name")).IsTrue();
    }
}
