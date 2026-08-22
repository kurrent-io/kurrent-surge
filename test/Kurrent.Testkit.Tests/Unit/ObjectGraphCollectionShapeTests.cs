// ReSharper disable ConvertToLocalFunction

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Kurrent.Testkit.Tests;

[Category("Testing")]
public class ObjectGraphCollectionShapeTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Sequences, whatever the implementation <<-

    [Test]
    public async ValueTask list_and_array_pass() {
        string[] words = [Faker.Lorem.Word(), Faker.Lorem.Word()];

        await Assert.That(words.ToList()).IsDeepEquivalentTo(words);
    }

    [Test]
    public async ValueTask array_and_immutable_array_pass() {
        int[] numbers = [Faker.Random.Int(1, 100), Faker.Random.Int(101, 200)];

        await Assert.That(numbers).IsDeepEquivalentTo(numbers.ToImmutableArray());
    }

    [Test]
    public async ValueTask list_and_immutable_list_pass() {
        var words = new List<string> { Faker.Lorem.Word(), Faker.Lorem.Word() };

        await Assert.That(words).IsDeepEquivalentTo(words.ToImmutableList());
    }

    [Test]
    public async ValueTask list_and_read_only_collection_pass() {
        var words = new List<string> { Faker.Lorem.Word(), Faker.Lorem.Word() };

        await Assert.That(words).IsDeepEquivalentTo(new ReadOnlyCollection<string>(words));
    }

    [Test]
    public async ValueTask linq_projections_over_different_collection_types_pass() {
        var words = new List<string> { Faker.Lorem.Word(), Faker.Lorem.Word() };

        IList<string> asInterface = words;

        await Assert.That(words.Select(word => word.Length)).IsDeepEquivalentTo(asInterface.Select(word => word.Length));
    }

    [Test]
    public async ValueTask a_member_holding_a_different_collection_type_passes() {
        var id    = Faker.Random.Guid().ToString();
        string[] items = [Faker.Commerce.ProductName(), Faker.Commerce.ProductName()];

        var actual   = new Basket(id, items.ToList());
        var expected = new Basket(id, items);

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    #endregion // Sequences, whatever the implementation

    #region ->> Order still counts <<-

    [Test]
    public async ValueTask reordered_elements_fail_across_collection_types() {
        int[] numbers = [1, 2, 3];

        var action = async () => await Assert.That(numbers.Reverse().ToList()).IsDeepEquivalentTo(numbers);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask different_counts_fail_across_collection_types() {
        int[] numbers = [1, 2, 3];

        var action = async () => await Assert.That(numbers.Take(2).ToList()).IsDeepEquivalentTo(numbers);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask a_list_of_key_value_pairs_keeps_its_order() {
        var expected = new List<KeyValuePair<string, int>> { new("a", 1), new("b", 2) };
        var actual   = new List<KeyValuePair<string, int>> { new("b", 2), new("a", 1) };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    #endregion // Order still counts

    #region ->> Dictionaries, whatever the implementation <<-

    [Test]
    public async ValueTask dictionary_and_immutable_dictionary_pass() {
        var entries = new Dictionary<string, int> { ["first"] = 1, ["second"] = 2 };

        await Assert.That(entries).IsDeepEquivalentTo(entries.ToImmutableDictionary());
    }

    [Test]
    public async ValueTask dictionary_and_frozen_dictionary_pass() {
        var entries = new Dictionary<string, int> { ["first"] = 1, ["second"] = 2 };

        await Assert.That(entries).IsDeepEquivalentTo(entries.ToFrozenDictionary());
    }

    [Test]
    public async ValueTask dictionary_and_sorted_dictionary_pass() {
        var entries = new Dictionary<string, int> { ["zulu"] = 1, ["alpha"] = 2 };

        await Assert.That(entries).IsDeepEquivalentTo(new SortedDictionary<string, int>(entries));
    }

    [Test]
    public async ValueTask dictionary_entries_are_matched_by_key_not_position() {
        var expected = new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2, ["charlie"] = 3 };
        var actual   = new Dictionary<string, int> { ["charlie"] = 3, ["alpha"] = 1, ["bravo"] = 2 };

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask a_missing_key_fails() {
        var expected = new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2 };
        var actual   = new Dictionary<string, int> { ["alpha"] = 1, ["delta"] = 2 };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask a_differing_value_fails() {
        var expected = new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2 };
        var actual   = new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 99 };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask a_dictionary_with_a_different_count_fails() {
        var expected = new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2 };
        var actual   = new Dictionary<string, int> { ["alpha"] = 1 };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    #endregion // Dictionaries, whatever the implementation

    #region ->> Guards <<-

    [Test]
    public async ValueTask a_string_and_a_character_collection_fail() {
        var action = async () => await Assert.That("abc").IsDeepEquivalentTo(new[] { 'a', 'b', 'c' });

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask two_equal_strings_still_pass() {
        var word = Faker.Lorem.Word();

        await Assert.That(word).IsDeepEquivalentTo(new string(word.AsSpan()));
    }

    #endregion // Guards

    #region ->> Ordering options <<-

    [Test]
    public async ValueTask ignoring_collection_order_changes_nothing_for_a_dictionary() {
        var expected = new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2 };
        var actual   = new Dictionary<string, int> { ["bravo"] = 2, ["alpha"] = 1 };

        await Assert.That(actual).IsDeepEquivalentTo(expected, cfg => cfg.IgnoringCollectionOrder());
    }

    [Test]
    public async ValueTask strict_ordering_compares_dictionary_entries_by_position() {
        var expected = new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2 };
        var actual   = new Dictionary<string, int> { ["bravo"] = 2, ["alpha"] = 1 };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected, cfg => cfg.WithStrictOrdering());

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask strict_ordering_for_a_member_compares_that_dictionary_by_position() {
        var id = Faker.Random.Guid().ToString();

        var expected = new Ledger(id, new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2 });
        var actual   = new Ledger(id, new Dictionary<string, int> { ["bravo"] = 2, ["alpha"] = 1 });

        var action = async () => await Assert.That(actual)
            .IsDeepEquivalentTo(expected, cfg => cfg.WithStrictOrderingFor(nameof(Ledger.Counters)));

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask without_strict_ordering_for_a_member_restores_key_matching() {
        var id = Faker.Random.Guid().ToString();

        var expected = new Ledger(id, new Dictionary<string, int> { ["alpha"] = 1, ["bravo"] = 2 });
        var actual   = new Ledger(id, new Dictionary<string, int> { ["bravo"] = 2, ["alpha"] = 1 });

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.WithStrictOrdering().WithoutStrictOrderingFor(nameof(Ledger.Counters))
        );
    }

    [Test]
    public async ValueTask strict_ordering_overrides_ignoring_collection_order_for_sequences() {
        int[] numbers = [1, 2, 3];

        var reordered = numbers.Reverse().ToList();

        await Assert.That(reordered).IsDeepEquivalentTo(numbers, cfg => cfg.IgnoringCollectionOrder());

        var action = async () => await Assert.That(reordered)
            .IsDeepEquivalentTo(numbers, cfg => cfg.IgnoringCollectionOrder().WithStrictOrdering());

        await Assert.That(action).ThrowsException();
    }

    #endregion // Ordering options

    sealed record Basket(string Id, IReadOnlyList<string> Items);

    sealed record Ledger(string Id, IReadOnlyDictionary<string, int> Counters);
}
