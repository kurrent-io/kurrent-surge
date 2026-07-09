using Bogus;

namespace Kurrent.Surge.Tests.Model;

public class MetadataTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Lock / Unlock <<-

    [Test]
    public async ValueTask metadata_can_be_locked() {
        // Arrange
        var metadata = new Metadata();

        // Act
        var locked = metadata.Lock();

        // Assert
        await Assert.That(locked.IsLocked).IsTrue();
        await Assert.That(locked).IsSameReferenceAs(metadata);
    }

    [Test]
    public async ValueTask locked_metadata_throws_on_indexer_set() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var value    = Faker.Random.Words();
        var metadata = new Metadata().Lock();

        // Act & Assert
        await Assert.That(() => metadata[key] = value).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.");
    }

    [Test]
    public async ValueTask locked_metadata_throws_on_remove() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var metadata = new Metadata().With(key, Faker.Random.Words()).Lock();

        // Act & Assert
        await Assert.That(() => metadata.Remove(key)).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.");
    }

    [Test]
    public async ValueTask locked_metadata_throws_on_with() {
        // Arrange
        var locked = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words()).Lock();

        // Act & Assert
        await Assert.That(() => locked.With(Faker.Random.AlphaNumeric(8), Faker.Random.Words())).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.");
    }

    [Test]
    public async ValueTask locked_metadata_throws_on_with_generic() {
        // Arrange
        var locked = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Int()).Lock();

        // Act & Assert
        await Assert.That(() => locked.With<int>(Faker.Random.AlphaNumeric(8), Faker.Random.Int())).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.");
    }

    [Test]
    public async ValueTask locked_metadata_throws_on_with_many() {
        // Arrange
        var locked     = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words()).Lock();
        var additional = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Act & Assert
        await Assert.That(() => locked.WithMany(additional)).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.");
    }

    [Test]
    public async ValueTask locked_metadata_throws_on_reset() {
        // Arrange
        var locked = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words()).Lock();
        var items  = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Act & Assert
        await Assert.That(() => locked.Reset(items)).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.");
    }

    [Test]
    public async ValueTask locked_metadata_throws_on_evolve_value() {
        // Arrange
        var key    = Faker.Random.AlphaNumeric(8);
        var locked = new Metadata().With(key, Faker.Random.Int()).Lock();

        // Act & Assert
        await Assert.That(() => locked.EvolveValue<int, int>(key, v => v * 2)).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.");
    }

    #endregion

    #region ->> CreateUnlockedCopy <<-

    [Test]
    public async ValueTask create_unlocked_copy_returns_independent_mutable_copy() {
        // Arrange
        var key    = Faker.Random.AlphaNumeric(8);
        var value  = Faker.Random.Words();
        var locked = new Metadata().With(key, value).Lock();

        // Act
        var unlocked = locked.CreateUnlockedCopy();

        // Assert
        await Assert.That(unlocked).IsNotSameReferenceAs(locked);
        await Assert.That(unlocked.IsLocked).IsFalse();
        await Assert.That(locked.IsLocked).IsTrue();
        await Assert.That(unlocked.ContainsKey(key)).IsTrue();
        await Assert.That(unlocked[key]).IsEqualTo(value);
    }

    [Test]
    public async ValueTask create_unlocked_copy_from_unlocked_returns_independent_copy() {
        // Arrange
        var unlocked = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Act
        var copy = unlocked.CreateUnlockedCopy();

        // Assert
        await Assert.That(copy).IsNotSameReferenceAs(unlocked);
        await Assert.That(copy.IsLocked).IsFalse();
    }

    [Test]
    public async ValueTask unlocked_copy_can_be_modified_without_affecting_original() {
        // Arrange
        var key    = Faker.Random.AlphaNumeric(8);
        var locked = new Metadata().With(key, Faker.Random.Words()).Lock();

        // Act
        var unlocked = locked.CreateUnlockedCopy();
        var modified = unlocked.With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Assert
        await Assert.That(modified).IsSameReferenceAs(unlocked);
        await Assert.That(modified.Count).IsEqualTo(2);
        await Assert.That(locked.Count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask can_lock_after_create_unlocked_copy() {
        // Arrange
        var locked = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words()).Lock();
        var copy   = locked.CreateUnlockedCopy();

        // Act
        var newLocked = copy.Lock();

        // Assert
        await Assert.That(newLocked).IsSameReferenceAs(copy);
        await Assert.That(newLocked.IsLocked).IsTrue();
        await Assert.That(() => newLocked[Faker.Random.AlphaNumeric(8)] = Faker.Random.Words()).ThrowsExactly<InvalidOperationException>();
    }

    #endregion

    #region ->> Mutation Methods <<-

    [Test]
    public async ValueTask with_modifies_self_when_unlocked() {
        // Arrange
        var metadata = new Metadata();
        var key      = Faker.Random.AlphaNumeric(8);
        var value    = Faker.Random.Words();

        // Act
        var result = metadata.With(key, value);

        // Assert
        await Assert.That(result).IsSameReferenceAs(metadata);
        await Assert.That(result.IsLocked).IsFalse();
        await Assert.That(result[key]).IsEqualTo(value);
    }

    [Test]
    public async ValueTask reset_replaces_contents() {
        // Arrange
        var metadata  = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());
        var newKey    = Faker.Random.AlphaNumeric(8);
        var newValue  = Faker.Random.Words();
        var newItems  = new Dictionary<string, object?> { [newKey] = newValue };

        // Act
        var result = metadata.Reset(newItems);

        // Assert
        await Assert.That(result).IsSameReferenceAs(metadata);
        await Assert.That(result.ContainsKey(newKey)).IsTrue();
        await Assert.That(result[newKey]).IsEqualTo(newValue);
    }

    [Test]
    public async ValueTask evolve_value_transforms_existing_value() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var original = Faker.Random.Int(1, 100);
        var metadata = new Metadata().With(key, original);

        // Act
        var result = metadata.EvolveValue<int, int>(key, v => v * 2);

        // Assert
        var expected = original * 2;
        await Assert.That(result).IsSameReferenceAs(metadata);
        await Assert.That(result.GetRequiredValue<int>(key)).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask evolve_value_does_nothing_for_missing_key() {
        // Arrange
        var metadata = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());
        var expected = metadata.Count;

        // Act
        metadata.EvolveValue<int, int>(Faker.Random.AlphaNumeric(8), v => v * 2);

        // Assert
        await Assert.That(metadata.Count).IsEqualTo(expected);
    }

    #endregion

    #region ->> Key Validation <<-

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("\t")]
    public async ValueTask with_rejects_empty_or_whitespace_keys(string key) {
        // Arrange
        var metadata = new Metadata();

        // Act & Assert
        await Assert.That(() => metadata.With(key, Faker.Random.Words())).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public async ValueTask indexer_set_rejects_empty_or_whitespace_keys(string key) {
        // Arrange
        var metadata = new Metadata();

        // Act & Assert
        await Assert.That(() => metadata[key] = Faker.Random.Words()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async ValueTask with_many_rejects_empty_or_whitespace_keys() {
        // Arrange
        var metadata = new Metadata();
        var items    = new KeyValuePair<string, object?>[] { new(" ", Faker.Random.Words()) };

        // Act & Assert
        await Assert.That(() => metadata.WithMany(items)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async ValueTask constructor_rejects_empty_or_whitespace_keys() {
        // Arrange
        var items = new Dictionary<string, object?> { [" "] = Faker.Random.Words() };

        // Act & Assert
        await Assert.That(() => new Metadata(items)).ThrowsExactly<ArgumentException>();
    }

    #endregion

    #region ->> Read Operations <<-

    [Test]
    public async ValueTask indexer_get_works_on_locked_metadata() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var value    = Faker.Random.Words();
        var metadata = new Metadata().With(key, value).Lock();

        // Act
        var result = metadata[key];

        // Assert
        await Assert.That(result).IsEqualTo(value);
    }

    [Test]
    public async ValueTask enumeration_works_on_locked_metadata() {
        // Arrange
        var key1   = Faker.Random.AlphaNumeric(8);
        var value1 = Faker.Random.Words();
        var key2   = Faker.Random.AlphaNumeric(8);
        var value2 = Faker.Random.Words();

        var metadata = new Metadata()
            .With(key1, value1)
            .With(key2, value2)
            .Lock();

        // Act
        var items = metadata.ToList();

        // Assert
        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(items).Contains(kvp => kvp.Key == key1 && kvp.Value!.ToString() == value1);
        await Assert.That(items).Contains(kvp => kvp.Key == key2 && kvp.Value!.ToString() == value2);
    }

    [Test]
    public async ValueTask readonly_operations_work_on_locked_metadata() {
        // Arrange
        var stringKey   = Faker.Random.AlphaNumeric(8);
        var stringValue = Faker.Random.Words();
        var intKey      = Faker.Random.AlphaNumeric(8);
        var intValue    = Faker.Random.Int();
        var defaultVal  = Faker.Random.Words();

        var metadata = new Metadata()
            .With(stringKey, stringValue)
            .With(intKey, intValue)
            .Lock();

        // Assert
        await Assert.That(metadata.Count).IsEqualTo(2);
        await Assert.That(metadata.ContainsKey(stringKey)).IsTrue();
        await Assert.That(metadata.ContainsKey(Faker.Random.AlphaNumeric(12))).IsFalse();
        await Assert.That(metadata.GetRequiredValue<string>(stringKey)).IsEqualTo(stringValue);
        await Assert.That(metadata.GetRequiredValue<int>(intKey)).IsEqualTo(intValue);
        await Assert.That(metadata.GetValueOrDefault<string>(Faker.Random.AlphaNumeric(12), defaultVal)).IsEqualTo(defaultVal);
        await Assert.That(metadata.TryGetValue<string>(stringKey, out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(stringValue);
    }

    [Test]
    public async ValueTask get_required_value_throws_on_missing_key() {
        // Arrange
        var metadata = new Metadata();

        // Act & Assert
        await Assert.That(() => metadata.GetRequiredValue<string>(Faker.Random.AlphaNumeric(8)))
            .ThrowsExactly<KeyNotFoundException>();
    }

    [Test]
    public async ValueTask keys_and_values_reflect_contents() {
        // Arrange
        var key1   = Faker.Random.AlphaNumeric(8);
        var key2   = Faker.Random.AlphaNumeric(8);
        var value1 = Faker.Random.Words();
        var value2 = Faker.Random.Words();

        var metadata = new Metadata().With(key1, value1).With(key2, value2);

        // Assert
        await Assert.That(metadata.Keys).Contains(key1);
        await Assert.That(metadata.Keys).Contains(key2);
        await Assert.That(metadata.Values).Contains(value1);
        await Assert.That(metadata.Values).Contains(value2);
    }

    #endregion

    #region ->> String Conversion <<-

    [Test]
    public async ValueTask try_get_value_converts_string_to_int() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var expected = Faker.Random.Int();
        var metadata = new Metadata().With(key, expected.ToString());

        // Act
        var found = metadata.TryGetValue<int>(key, out var value);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_get_value_converts_string_to_nullable_int() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var expected = Faker.Random.Int();
        var metadata = new Metadata().With(key, expected.ToString());

        // Act
        var found = metadata.TryGetValue<int?>(key, out var value);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_get_value_converts_string_to_guid() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var expected = Faker.Random.Guid();
        var metadata = new Metadata().With(key, expected.ToString());

        // Act
        var found = metadata.TryGetValue<Guid>(key, out var value);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_get_value_converts_string_to_datetimeoffset() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var expected = Faker.Date.RecentOffset();
        var metadata = new Metadata().With(key, expected.ToString("O"));

        // Act
        var found = metadata.TryGetValue<DateTimeOffset>(key, out var value);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_get_value_converts_string_to_enum_case_insensitive() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var expected = Faker.PickRandom<DayOfWeek>();
        var metadata = new Metadata().With(key, expected.ToString().ToLowerInvariant());

        // Act
        var found = metadata.TryGetValue<DayOfWeek>(key, out var value);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_get_value_returns_false_for_unparseable_string() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var metadata = new Metadata().With(key, Faker.Random.Words());

        // Act
        var found = metadata.TryGetValue<int>(key, out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async ValueTask try_get_value_returns_false_for_unsupported_type() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var metadata = new Metadata().With(key, "1,2");

        // Act — TestCoordinate is outside the built-in conversion set; TryGetParsable is the supported path.
        var found = metadata.TryGetValue<TestCoordinate>(key, out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async ValueTask try_get_parsable_parses_string_via_iparsable() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var expected = new TestCoordinate(Faker.Random.Int(), Faker.Random.Int());
        var metadata = new Metadata().With(key, $"{expected.X},{expected.Y}");

        // Act
        var found = metadata.TryGetParsable<TestCoordinate>(key, out var value);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_get_parsable_returns_typed_value_directly() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var expected = new TestCoordinate(Faker.Random.Int(), Faker.Random.Int());
        var metadata = new Metadata().With(key, expected);

        // Act
        var found = metadata.TryGetParsable<TestCoordinate>(key, out var value);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_get_parsable_returns_false_for_unparseable_string() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var metadata = new Metadata().With(key, Faker.Random.Words());

        // Act
        var found = metadata.TryGetParsable<TestCoordinate>(key, out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    readonly record struct TestCoordinate(int X, int Y) : IParsable<TestCoordinate> {
        public static TestCoordinate Parse(string s, IFormatProvider? provider) =>
            TryParse(s, provider, out var result) ? result : throw new FormatException();

        public static bool TryParse(string? s, IFormatProvider? provider, out TestCoordinate result) {
            result = default;

            if (s?.Split(',') is not [var first, var second])
                return false;

            if (!int.TryParse(first, out var x) || !int.TryParse(second, out var y))
                return false;

            result = new(x, y);
            return true;
        }
    }

    #endregion

    #region ->> Constructors and Factories <<-

    [Test]
    public async ValueTask constructor_from_enumerable_copies_items() {
        // Arrange
        var key   = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Words();
        var items = new Dictionary<string, object?> { [key] = value };

        // Act
        var metadata = new Metadata(items);

        // Assert
        await Assert.That(metadata.Count).IsEqualTo(1);
        await Assert.That(metadata[key]).IsEqualTo(value);
    }

    [Test]
    public async ValueTask constructor_from_metadata_copies_items() {
        // Arrange
        var key      = Faker.Random.AlphaNumeric(8);
        var value    = Faker.Random.Words();
        var original = new Metadata().With(key, value);

        // Act
        var copy = new Metadata(original);

        // Assert
        await Assert.That(copy).IsNotSameReferenceAs(original);
        await Assert.That(copy[key]).IsEqualTo(value);
    }

    [Test]
    public async ValueTask static_new_returns_empty_metadata() {
        // Act
        var metadata = Metadata.New;

        // Assert
        await Assert.That(metadata.Count).IsEqualTo(0);
        await Assert.That(metadata.IsLocked).IsFalse();
    }

    [Test]
    public async ValueTask static_create_from_dictionary() {
        // Arrange
        var key   = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Words();
        var dict  = new Dictionary<string, object?> { [key] = value };

        // Act
        var metadata = Metadata.Create(dict);

        // Assert
        await Assert.That(metadata.Count).IsEqualTo(1);
        await Assert.That(metadata[key]).IsEqualTo(value);
    }

    [Test]
    public async ValueTask implicit_conversion_from_dictionary() {
        // Arrange
        var key   = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Words();
        var dict  = new Dictionary<string, object?> { [key] = value };

        // Act
        Metadata metadata = dict;

        // Assert
        await Assert.That(metadata.Count).IsEqualTo(1);
        await Assert.That(metadata[key]).IsEqualTo(value);
    }

    #endregion

    #region ->> Stringify <<-

    [Test]
    public async ValueTask stringify_converts_common_types_to_strings() {
        // Arrange
        var dateTime       = Faker.Date.Recent();
        var dateTimeOffset = new DateTimeOffset(Faker.Date.Recent());
        var intValue       = Faker.Random.Int();
        var guidValue      = Faker.Random.Guid();
        var stringValue    = Faker.Random.Words();

        var metadata = new Metadata()
            .With("dateTime", dateTime)
            .With("dateTimeOffset", dateTimeOffset)
            .With("int", intValue)
            .With("guid", guidValue)
            .With("string", stringValue)
            .With("null", null);

        // Act
        var stringified = metadata.Stringify();

        // Assert
        await Assert.That(stringified).IsNotSameReferenceAs(metadata);
        await Assert.That(stringified.GetRequiredValue<string>("dateTime")).IsEqualTo(dateTime.ToString("O"));
        await Assert.That(stringified.GetRequiredValue<string>("dateTimeOffset")).IsEqualTo(dateTimeOffset.ToString("O"));
        await Assert.That(stringified.GetRequiredValue<string>("int")).IsEqualTo(intValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(stringified.GetRequiredValue<string>("guid")).IsEqualTo(guidValue.ToString("D"));
        await Assert.That(stringified.GetRequiredValue<string>("string")).IsEqualTo(stringValue);
        await Assert.That(stringified.TryGetValue<string>("null", out var nullVal)).IsTrue();
        await Assert.That(nullVal).IsNull();
    }

    [Test]
    public async ValueTask stringify_converts_byte_array_to_base64() {
        // Arrange
        var bytes    = Faker.Random.Bytes(16);
        var metadata = new Metadata().With("bytes", bytes);

        // Act
        var stringified = metadata.Stringify();

        // Assert
        await Assert.That(stringified.GetRequiredValue<string>("bytes")).IsEqualTo(Convert.ToBase64String(bytes));
    }

    #endregion

    #region ->> Equality <<-

    [Test]
    public async ValueTask equal_metadata_instances_are_equal() {
        // Arrange
        var key   = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Words();

        var a = new Metadata().With(key, value);
        var b = new Metadata().With(key, value);

        // Assert
        await Assert.That(a.Equals(b)).IsTrue();
        await Assert.That(a == b).IsTrue();
        await Assert.That(a != b).IsFalse();
        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }

    [Test]
    public async ValueTask different_metadata_instances_are_not_equal() {
        // Arrange
        var a = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());
        var b = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Assert
        await Assert.That(a.Equals(b)).IsFalse();
        await Assert.That(a == b).IsFalse();
        await Assert.That(a != b).IsTrue();
    }

    [Test]
    public async ValueTask metadata_equals_null_returns_false() {
        // Arrange
        var metadata = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Assert
        await Assert.That(metadata.Equals(null)).IsFalse();
        await Assert.That(metadata! == null).IsFalse();
        await Assert.That(metadata! != null).IsTrue();
    }

    [Test]
    public async ValueTask metadata_equals_same_reference_returns_true() {
        // Arrange
        var metadata = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Assert
        await Assert.That(metadata.Equals(metadata)).IsTrue();
        await Assert.That(metadata.Equals((object)metadata)).IsTrue();
    }

    [Test]
    public async ValueTask metadata_equals_different_type_returns_false() {
        // Arrange
        var metadata = new Metadata().With(Faker.Random.AlphaNumeric(8), Faker.Random.Words());

        // Assert
        await Assert.That(metadata.Equals(Faker.Random.Words())).IsFalse();
    }

    [Test]
    public async ValueTask equality_is_case_insensitive_on_keys() {
        // Arrange
        var key   = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Words();

        var a = new Metadata().With(key.ToLower(), value);
        var b = new Metadata().With(key.ToUpper(), value);

        // Assert
        await Assert.That(a.Equals(b)).IsTrue();
        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }

    [Test]
    public async ValueTask empty_metadata_instances_are_equal() {
        // Arrange
        var a = new Metadata();
        var b = new Metadata();

        // Assert
        await Assert.That(a.Equals(b)).IsTrue();
        await Assert.That(a == b).IsTrue();
        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }

    #endregion

    #region ->> ToString <<-

    [Test]
    public async ValueTask to_string_contains_count_and_entries() {
        // Arrange
        var key   = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Words();
        var metadata = new Metadata().With(key, value);

        // Act
        var result = metadata.ToString();

        // Assert
        await Assert.That(result).Contains("1 items");
        await Assert.That(result).Contains(key);
        await Assert.That(result).Contains(value);
    }

    #endregion
}
