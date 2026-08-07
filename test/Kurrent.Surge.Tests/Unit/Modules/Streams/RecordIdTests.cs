using System.Text;
using System.Text.Json;
using Bogus;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Modules.Streams;

/// <summary>
/// Coverage for the [UniqueIdentifier]-generated RecordId surface: minting, dual-face
/// parsing (sortable form or GUID), byte and Guid conversions, span/UTF-8 formatting,
/// and time-ordered comparison.
/// </summary>
public class RecordIdTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask new_mints_id_with_timestamp_near_now() {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var id = RecordId.New();

        // Assert
        await Assert.That(id.Timestamp).IsEqualTo(before).Within(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async ValueTask new_with_explicit_timestamp_stores_millisecond_truncated_time() {
        // Arrange
        var timestamp = Faker.Date.RecentOffset();

        // Act
        var id = RecordId.New(timestamp);

        // Assert
        // Ulid's timestamp component has millisecond resolution, so the round trip loses
        // sub-millisecond precision.
        await Assert.That(id.Timestamp).IsEqualTo(timestamp).Within(TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async ValueTask new_with_timestamp_and_randomness_is_deterministic() {
        // Arrange
        var timestamp  = Faker.Date.RecentOffset();
        var randomness = Faker.Random.Bytes(10);

        // Act
        var first  = RecordId.New(timestamp, randomness);
        var second = RecordId.New(timestamp, randomness);

        // Assert
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async ValueTask from_guid_and_to_guid_round_trip() {
        // Arrange
        var guid = Faker.Random.Guid();

        // Act
        var id = RecordId.From(guid);

        // Assert
        await Assert.That(id.ToGuid()).IsEqualTo(guid);
    }

    [Test]
    public async ValueTask serializes_and_deserializes_via_system_text_json() {
        // Arrange
        // The generator attaches [JsonConverter(typeof(UniqueIdentifierJsonConverter<T>))]
        // directly to the generated partial — no manual registration required.
        var id = RecordId.New();

        // Act
        var json      = JsonSerializer.Serialize(id);
        var roundTrip = JsonSerializer.Deserialize<RecordId>(json);

        // Assert
        await Assert.That(json).IsEqualTo($"\"{id}\"");
        await Assert.That(roundTrip).IsEqualTo(id);
    }

    [Test]
    public async ValueTask to_guid_string_matches_the_wire_face() {
        // Arrange
        var id = RecordId.New();

        // Act
        var wireForm = id.ToGuidString();

        // Assert
        await Assert.That(wireForm).IsEqualTo(id.ToGuid().ToString());
    }

    [Test]
    public async ValueTask from_wire_string_reverses_to_guid_string() {
        // Arrange
        var id       = RecordId.New();
        var wireForm = id.ToGuidString();

        // Act
        var roundTrip = RecordId.FromWireString(wireForm);

        // Assert
        await Assert.That(roundTrip).IsEqualTo(id);
    }

    [Test]
    public async ValueTask from_bytes_and_to_byte_array_round_trip() {
        // Arrange
        var original = RecordId.New();
        var bytes    = original.ToByteArray();

        // Act
        var id = RecordId.From(bytes);

        // Assert
        await Assert.That(id).IsEqualTo(original);
    }

    [Test]
    public async ValueTask try_write_bytes_matches_to_byte_array() {
        // Arrange
        var id          = RecordId.New();
        var expected    = id.ToByteArray();
        var destination = new byte[16];

        // Act
        var wrote = id.TryWriteBytes(destination);

        // Assert
        await Assert.That(wrote).IsTrue();
        await Assert.That(destination).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask try_parse_accepts_the_sortable_form_it_produced() {
        // Arrange
        var id       = RecordId.New();
        var sortable = id.ToString();

        // Act
        var parsed = RecordId.TryParse(sortable, out var result);

        // Assert
        await Assert.That(parsed).IsTrue();
        await Assert.That(result).IsEqualTo(id);
    }

    [Test]
    public async ValueTask try_parse_accepts_guid_format() {
        // Arrange
        var guid     = Faker.Random.Guid();
        var expected = RecordId.From(guid);

        // Act
        var parsed = RecordId.TryParse(guid.ToString(), out var result);

        // Assert
        await Assert.That(parsed).IsTrue();
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_parse_rejects_invalid_input() {
        // Arrange
        const string input = "not-a-valid-record-id";

        // Act
        var parsed = RecordId.TryParse(input, out var result);

        // Assert
        await Assert.That(parsed).IsFalse();
        await Assert.That(result).IsEqualTo(RecordId.None);
    }

    [Test]
    public async ValueTask parse_throws_format_exception_for_invalid_input() {
        // Arrange
        const string input = "not-a-valid-record-id";

        // Act & Assert
        await Assert.That(() => RecordId.Parse(input)).Throws<FormatException>();
    }

    [Test]
    public async ValueTask parse_span_and_string_overloads_agree() {
        // Arrange
        var id   = RecordId.New();
        var text = id.ToString();

        // Act
        var fromString = RecordId.Parse(text);
        var fromSpan   = RecordId.Parse(text.AsSpan());

        // Assert
        await Assert.That(fromString).IsEqualTo(id);
        await Assert.That(fromSpan).IsEqualTo(id);
    }

    [Test]
    public async ValueTask try_format_chars_matches_to_string() {
        // Arrange
        var id          = RecordId.New();
        var expected    = id.ToString();
        var destination = new char[26];

        // Act
        var formatted = id.TryFormat(destination, out var charsWritten);

        // Assert
        await Assert.That(formatted).IsTrue();
        await Assert.That(charsWritten).IsEqualTo(26);
        await Assert.That(new string(destination)).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_format_utf8_bytes_match_char_form() {
        // Arrange
        var id          = RecordId.New();
        var expected    = id.ToString();
        var destination = new byte[26];

        // Act
        var formatted = id.TryFormat(destination, out var bytesWritten);

        // Assert
        await Assert.That(formatted).IsTrue();
        await Assert.That(bytesWritten).IsEqualTo(26);
        await Assert.That(Encoding.UTF8.GetString(destination)).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask compare_to_orders_by_minting_time() {
        // Arrange
        var earlierTimestamp = Faker.Date.RecentOffset();
        var earlier          = RecordId.New(earlierTimestamp);
        var later            = RecordId.New(earlierTimestamp.AddSeconds(5));

        // Act
        var earlierBeforeLater = earlier.CompareTo(later);
        var laterAfterEarlier  = later.CompareTo(earlier);

        // Assert
        await Assert.That(earlierBeforeLater).IsLessThan(0);
        await Assert.That(laterAfterEarlier).IsGreaterThan(0);
        await Assert.That(earlier.CompareTo(earlier)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask none_is_the_default_value() {
        // Act & Assert
        await Assert.That(RecordId.None).IsEqualTo(default(RecordId));
        await Assert.That(RecordId.None.ToGuid()).IsEqualTo(Guid.Empty);
    }
}
