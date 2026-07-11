using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Tests.Internal;

public class ConcurrentBidirectionalDictionaryTests : IDisposable {
    readonly ConcurrentBidirectionalDictionary<string, int> _sut = new();

    public void Dispose() => _sut.Dispose();

    [Test]
    public async Task constructor_with_capacity_and_concurrency_should_initialize_correctly() {
        // Arrange
        var concurrencyLevel = 2;
        var capacity         = 100;

        // Act
        using var sut = new ConcurrentBidirectionalDictionary<string, int>(concurrencyLevel, capacity);
        sut.Add("one", 1);

        // Assert
        await Assert.That(sut.Count).IsEqualTo(1);
        await Assert.That(sut.ContainsKey("one")).IsTrue();
    }

    [Test]
    public async Task add_single_item_should_succeed_and_be_retrievable() {
        // Arrange
        var key   = "one";
        var value = 1;

        // Act
        _sut.Add(key, value);

        // Assert
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut.ContainsKey(key)).IsTrue();
        await Assert.That(_sut.ContainsValue(value)).IsTrue();

        _sut.TryGetValue(key, out var retrievedValue);
        await Assert.That(retrievedValue).IsEqualTo(value);

        _sut.TryGetKey(value, out var retrievedKey);
        await Assert.That(retrievedKey).IsEqualTo(key);

        await Assert.That(_sut[key]).IsEqualTo(value);
        await Assert.That(_sut.GetKeyByValue(value)).IsEqualTo(key);
    }

    [Test]
    public async Task add_multiple_items_should_succeed_and_be_retrievable() {
        // Arrange & Act
        _sut.Add("one", 1);
        _sut.Add("two", 2);
        _sut.Add("three", 3);

        // Assert
        await Assert.That(_sut.Count).IsEqualTo(3);
        await Assert.That(_sut["one"]).IsEqualTo(1);
        await Assert.That(_sut.GetKeyByValue(1)).IsEqualTo("one");
        await Assert.That(_sut["two"]).IsEqualTo(2);
        await Assert.That(_sut.GetKeyByValue(2)).IsEqualTo("two");
        await Assert.That(_sut["three"]).IsEqualTo(3);
        await Assert.That(_sut.GetKeyByValue(3)).IsEqualTo("three");
    }

    [Test]
    public async Task try_add_new_item_should_succeed_and_return_true() {
        // Arrange
        var key   = "one";
        var value = 1;

        // Act
        var result = _sut.TryAdd(key, value);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut[key]).IsEqualTo(value);
        await Assert.That(_sut.GetKeyByValue(value)).IsEqualTo(key);
    }

    [Test]
    public async Task remove_existing_item_by_key_should_succeed_and_remove_both_mappings() {
        // Arrange
        _sut.Add("one", 1);
        _sut.Add("two", 2);

        // Act
        var result = _sut.Remove("one");

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut.ContainsKey("one")).IsFalse();
        await Assert.That(_sut.ContainsValue(1)).IsFalse();
        await Assert.That(_sut.ContainsKey("two")).IsTrue(); // Ensure other item remains
        await Assert.That(_sut.ContainsValue(2)).IsTrue();
    }

    [Test]
    public async Task remove_existing_item_by_value_should_succeed_and_remove_both_mappings() {
        // Arrange
        _sut.Add("one", 1);
        _sut.Add("two", 2);

        // Act
        var result = _sut.RemoveByValue(1);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut.ContainsKey("one")).IsFalse();
        await Assert.That(_sut.ContainsValue(1)).IsFalse();
        await Assert.That(_sut.ContainsKey("two")).IsTrue(); // Ensure other item remains
        await Assert.That(_sut.ContainsValue(2)).IsTrue();
    }

    [Test]
    public async Task remove_non_existing_item_by_key_should_return_false() {
        // Arrange
        _sut.Add("one", 1);

        // Act
        var result = _sut.Remove("two");

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(_sut.Count).IsEqualTo(1);
    }

    [Test]
    public async Task remove_by_value_non_existing_item_should_return_false() {
        // Arrange
        _sut.Add("one", 1);

        // Act
        var result = _sut.RemoveByValue(2);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(_sut.Count).IsEqualTo(1);
    }

    [Test]
    public async Task indexer_set_add_new_item_should_succeed() {
        // Arrange
        var key   = "ten";
        var value = 10;

        // Act
        _sut[key] = value;

        // Assert
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut[key]).IsEqualTo(value);
        await Assert.That(_sut.GetKeyByValue(value)).IsEqualTo(key);
    }

    [Test]
    public async Task indexer_set_update_existing_item_should_update_both_mappings() {
        // Arrange
        _sut.Add("ten", 10);
        _sut.Add("twenty", 20);
        var newKey   = "ten";
        var newValue = 11; // New value for existing key "ten"

        // Act
        _sut[newKey] = newValue;

        // Assert
        await Assert.That(_sut.Count).IsEqualTo(2);                        // Count remains the same
        await Assert.That(_sut[newKey]).IsEqualTo(newValue);               // Forward map updated
        await Assert.That(_sut.ContainsValue(10)).IsFalse();        // Old value removed from reverse map
        await Assert.That(_sut.GetKeyByValue(newValue)).IsEqualTo(newKey); // New value added to reverse map
        await Assert.That(_sut.ContainsKey("twenty")).IsTrue();     // Other pair unaffected
        await Assert.That(_sut.GetKeyByValue(20)).IsEqualTo("twenty");
    }

    [Test]
    public async Task indexer_set_update_existing_item_with_value_already_mapped_should_throw_argument_exception() {
        // Arrange
        _sut.Add("ten", 10);
        _sut.Add("twenty", 20);

        // Act & Assert
        var ex = await Assert.That(() => _sut["ten"] = 20).ThrowsExactly<ArgumentException>(); // Try map "ten" to 20, which is already used by "twenty"
        await Assert.That(ex.Message).Contains("Value '20' already exists for key 'twenty'.");

        // Verify state hasn't changed inconsistently
        await Assert.That(_sut.Count).IsEqualTo(2);
        await Assert.That(_sut["ten"]).IsEqualTo(10);
        await Assert.That(_sut.GetKeyByValue(10)).IsEqualTo("ten");
        await Assert.That(_sut["twenty"]).IsEqualTo(20);
        await Assert.That(_sut.GetKeyByValue(20)).IsEqualTo("twenty");
    }

    [Test]
    public async Task indexer_get_existing_item_should_return_value() {
        // Arrange
        _sut.Add("one", 1);

        // Act
        var value = _sut["one"];

        // Assert
        await Assert.That(value).IsEqualTo(1);
    }

    [Test]
    public async Task indexer_get_non_existing_item_should_throw_key_not_found_exception() {
        // Arrange & Act & Assert
        await Assert.That(() => _sut["one"]).ThrowsExactly<KeyNotFoundException>();
    }

    [Test]
    public async Task get_key_by_value_existing_item_should_return_key() {
        // Arrange
        _sut.Add("one", 1);

        // Act
        var key = _sut.GetKeyByValue(1);

        // Assert
        await Assert.That(key).IsEqualTo("one");
    }

    [Test]
    public async Task get_key_by_value_non_existing_item_should_throw_key_not_found_exception() {
        // Arrange & Act & Assert
        await Assert.That(() => _sut.GetKeyByValue(1)).ThrowsExactly<KeyNotFoundException>();
    }

    [Test]
    public async Task add_duplicate_key_should_throw_argument_exception() {
        // Arrange
        _sut.Add("one", 1);

        // Act & Assert
        var ex = await Assert.That(() => _sut.Add("one", 2)).ThrowsExactly<ArgumentException>();
        await Assert.That(ex.ParamName).IsEqualTo("key");

        // Verify state
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut["one"]).IsEqualTo(1);
        await Assert.That(_sut.GetKeyByValue(1)).IsEqualTo("one");
    }

    [Test]
    public async Task add_duplicate_value_should_throw_argument_exception() {
        // Arrange
        _sut.Add("one", 1);

        // Act & Assert
        var ex = await Assert.That(() => _sut.Add("two", 1)).ThrowsExactly<ArgumentException>();
        await Assert.That(ex.ParamName).IsEqualTo("value");

        // Verify state
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut["one"]).IsEqualTo(1);
        await Assert.That(_sut.GetKeyByValue(1)).IsEqualTo("one");
    }

    [Test]
    public async Task try_add_duplicate_key_should_return_false() {
        // Arrange
        _sut.Add("one", 1);

        // Act
        var result = _sut.TryAdd("one", 2);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut["one"]).IsEqualTo(1); // Original value remains
    }

    [Test]
    public async Task try_add_duplicate_value_should_return_false() {
        // Arrange
        _sut.Add("one", 1);

        // Act
        var result = _sut.TryAdd("two", 1);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut["one"]).IsEqualTo(1); // Original key remains
    }

    // Test for adding null key is removed as it's now enforced by 'notnull' constraint at compile time.

    [Test]
    public async Task contains_key_existing_key_should_return_true() {
        _sut.Add("a", 1);
        await Assert.That(_sut.ContainsKey("a")).IsTrue();
    }

    [Test]
    public async Task contains_key_non_existing_key_should_return_false() {
        await Assert.That(_sut.ContainsKey("a")).IsFalse();
    }

    [Test]
    public async Task contains_value_existing_value_should_return_true() {
        _sut.Add("a", 1);
        await Assert.That(_sut.ContainsValue(1)).IsTrue();
    }

    [Test]
    public async Task contains_value_non_existing_value_should_return_false() {
        await Assert.That(_sut.ContainsValue(1)).IsFalse();
    }

    [Test]
    public async Task try_get_value_existing_key_should_return_true_and_value() {
        _sut.Add("a", 1);
        var result = _sut.TryGetValue("a", out var value);
        await Assert.That(result).IsTrue();
        await Assert.That(value).IsEqualTo(1);
    }

    [Test]
    public async Task try_get_value_non_existing_key_should_return_false_and_default_value() {
        var result = _sut.TryGetValue("a", out var value);
        await Assert.That(result).IsFalse();
        await Assert.That(value).IsEqualTo(default(int)); // 0 for int
    }

    [Test]
    public async Task try_get_key_existing_value_should_return_true_and_key() {
        _sut.Add("a", 1);
        var result = _sut.TryGetKey(1, out var key);
        await Assert.That(result).IsTrue();
        await Assert.That(key).IsEqualTo("a");
    }

    [Test]
    public async Task try_get_key_non_existing_value_should_return_false_and_default_key() {
        var result = _sut.TryGetKey(1, out var key);
        await Assert.That(result).IsFalse();
        await Assert.That(key).IsNull();
    }

    [Test]
    public async Task clear_non_empty_dictionary_should_remove_all_items() {
        // Arrange
        _sut.Add("one", 1);
        _sut.Add("two", 2);

        // Act
        _sut.Clear();

        // Assert
        await Assert.That(_sut.Count).IsEqualTo(0);
        await Assert.That(_sut.ContainsKey("one")).IsFalse();
        await Assert.That(_sut.ContainsValue(1)).IsFalse();
        await Assert.That(_sut.Keys).IsEmpty();
        await Assert.That(_sut.Values).IsEmpty();
        await Assert.That(_sut).IsEmpty(); // Checks GetEnumerator
    }

    [Test]
    public async Task count_should_reflect_number_of_items() {
        await Assert.That(_sut.Count).IsEqualTo(0);
        _sut.Add("a", 1);
        await Assert.That(_sut.Count).IsEqualTo(1);
        _sut.Add("b", 2);
        await Assert.That(_sut.Count).IsEqualTo(2);
        _sut.Remove("a");
        await Assert.That(_sut.Count).IsEqualTo(1);
        _sut.Clear();
        await Assert.That(_sut.Count).IsEqualTo(0);
    }

    [Test]
    public async Task keys_should_return_all_keys() {
        _sut.Add("one", 1);
        _sut.Add("two", 2);
        _sut.Add("three", 3);

        var keys = _sut.Keys;

        await Assert.That(keys).IsEquivalentTo(new[] { "one", "two", "three" });
        await Assert.That(keys.Count()).IsEqualTo(3); // Check count via enumeration
    }

    [Test]
    public async Task values_should_return_all_values() {
        _sut.Add("one", 1);
        _sut.Add("two", 2);
        _sut.Add("three", 3);

        var values = _sut.Values;

        await Assert.That(values).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(values.Count()).IsEqualTo(3); // Check count via enumeration
    }

    [Test]
    public async Task get_enumerator_should_iterate_over_all_pairs() {
        _sut.Add("one", 1);
        _sut.Add("two", 2);
        _sut.Add("three", 3);

        var pairs = new List<KeyValuePair<string, int>>();
        foreach (var kvp in _sut) pairs.Add(kvp);

        await Assert.That(pairs.Count).IsEqualTo(3);
        await Assert.That(pairs).Contains(kvp => kvp.Key == "one" && kvp.Value == 1);
        await Assert.That(pairs).Contains(kvp => kvp.Key == "two" && kvp.Value == 2);
        await Assert.That(pairs).Contains(kvp => kvp.Key == "three" && kvp.Value == 3);
    }

    [Test]
    public async Task custom_comparers_case_insensitive_should_work_correctly() {
        // Arrange
        using var sutCi = new ConcurrentBidirectionalDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase,
            StringComparer.OrdinalIgnoreCase
        );

        // Act & Assert
        sutCi.Add("KeyOne", "ValueOne");
        await Assert.That(sutCi.Count).IsEqualTo(1);

        // ContainsKey (case-insensitive)
        await Assert.That(sutCi.ContainsKey("keyone")).IsTrue();
        await Assert.That(sutCi.ContainsKey("KEYONE")).IsTrue();
        await Assert.That(sutCi.ContainsKey("KeyOne")).IsTrue();
        await Assert.That(sutCi.ContainsKey("KeyTwo")).IsFalse();

        // ContainsValue (case-insensitive)
        await Assert.That(sutCi.ContainsValue("valueone")).IsTrue();
        await Assert.That(sutCi.ContainsValue("VALUEONE")).IsTrue();
        await Assert.That(sutCi.ContainsValue("ValueOne")).IsTrue();
        await Assert.That(sutCi.ContainsValue("ValueTwo")).IsFalse();

        // TryGetValue (case-insensitive key)
        sutCi.TryGetValue("KEYONE", out var val);
        await Assert.That(val).IsEqualTo("ValueOne");

        // TryGetKey (case-insensitive value)
        sutCi.TryGetKey("VALUEONE", out var key);
        await Assert.That(key).IsEqualTo("KeyOne");

        // Indexer Get (case-insensitive key)
        await Assert.That(sutCi["keyone"]).IsEqualTo("ValueOne");

        // GetKeyByValue (case-insensitive value)
        await Assert.That(sutCi.GetKeyByValue("valueone")).IsEqualTo("KeyOne");

        // Add duplicate key (case-insensitive) - should fail
        await Assert.That(() => sutCi.Add("keyone", "ValueTwo")).ThrowsExactly<ArgumentException>();

        // Add duplicate value (case-insensitive) - should fail
        await Assert.That(() => sutCi.Add("KeyTwo", "valueone")).ThrowsExactly<ArgumentException>();

        // Indexer set update (case-insensitive key)
        sutCi["KEYONE"] = "NewValue";
        await Assert.That(sutCi.ContainsKey("KeyOne")).IsTrue();
        await Assert.That(sutCi.ContainsValue("ValueOne")).IsFalse();
        await Assert.That(sutCi.ContainsValue("NewValue")).IsTrue();
        await Assert.That(sutCi.GetKeyByValue("newvalue")).IsEqualTo("KEYONE"); // the value is still uppercase

        // Remove (case-insensitive key)
        await Assert.That(sutCi.Remove("keyone")).IsTrue();
        await Assert.That(sutCi.Count).IsEqualTo(0);
    }

    [Test]
    public async Task icollection_add_should_work() {
        ICollection<KeyValuePair<string, int>> coll = _sut;
        coll.Add(new KeyValuePair<string, int>("a", 1));

        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut["a"]).IsEqualTo(1);
    }

    [Test]
    public async Task icollection_contains_existing_item_should_return_true() {
        _sut.Add("a", 1);
        ICollection<KeyValuePair<string, int>> coll = _sut;
        await Assert.That(coll.Contains(new KeyValuePair<string, int>("a", 1))).IsTrue();
    }

    [Test]
    public async Task icollection_contains_non_existing_key_should_return_false() {
        _sut.Add("a", 1);
        ICollection<KeyValuePair<string, int>> coll = _sut;
        await Assert.That(coll.Contains(new KeyValuePair<string, int>("b", 1))).IsFalse();
    }

    [Test]
    public async Task icollection_contains_key_exists_but_wrong_value_should_return_false() {
        _sut.Add("a", 1);
        ICollection<KeyValuePair<string, int>> coll = _sut;
        await Assert.That(coll.Contains(new KeyValuePair<string, int>("a", 2))).IsFalse();
    }

    [Test]
    public async Task icollection_remove_existing_item_should_succeed() {
        _sut.Add("a", 1);
        _sut.Add("b", 2);
        ICollection<KeyValuePair<string, int>> coll = _sut;

        var result = coll.Remove(new KeyValuePair<string, int>("a", 1));

        await Assert.That(result).IsTrue();
        await Assert.That(_sut.Count).IsEqualTo(1);
        await Assert.That(_sut.ContainsKey("a")).IsFalse();
        await Assert.That(_sut.ContainsValue(1)).IsFalse();
        await Assert.That(_sut.ContainsKey("b")).IsTrue();
    }

    [Test]
    public async Task icollection_remove_item_with_wrong_value_should_return_false() {
        _sut.Add("a", 1);
        _sut.Add("b", 2);
        ICollection<KeyValuePair<string, int>> coll = _sut;

        var result = coll.Remove(new KeyValuePair<string, int>("a", 99)); // Key exists, value doesn't match

        await Assert.That(result).IsFalse();
        await Assert.That(_sut.Count).IsEqualTo(2); // Should not have removed anything
        await Assert.That(_sut["a"]).IsEqualTo(1);
        await Assert.That(_sut.GetKeyByValue(1)).IsEqualTo("a");
    }

    [Test]
    public async Task icollection_remove_non_existing_item_should_return_false() {
        _sut.Add("a", 1);
        ICollection<KeyValuePair<string, int>> coll = _sut;

        var result = coll.Remove(new KeyValuePair<string, int>("c", 3));

        await Assert.That(result).IsFalse();
        await Assert.That(_sut.Count).IsEqualTo(1);
    }

    [Test]
    public async Task icollection_copy_to_should_copy_all_items() {
        _sut.Add("one", 1);
        _sut.Add("two", 2);
        _sut.Add("three", 3);
        ICollection<KeyValuePair<string, int>> coll = _sut;

        var array = new KeyValuePair<string, int>[5]; // Larger array
        coll.CopyTo(array, 1);                        // Copy starting at index 1

        await Assert.That(array[0]).IsEqualTo(default(KeyValuePair<string, int>)); // Index 0 untouched
        await Assert.That(array[1..4]).IsEquivalentTo(new[] { Kvp("one", 1), Kvp("two", 2), Kvp("three", 3) });
        await Assert.That(array[4]).IsEqualTo(default(KeyValuePair<string, int>)); // Index 4 untouched
    }

    [Test]
    public async Task icollection_copy_to_throws_on_null_array() {
        ICollection<KeyValuePair<string, int>> coll = _sut;
        await Assert.That(() => coll.CopyTo(null!, 0)).ThrowsExactly<NullReferenceException>();
    }

    [Test]
    public async Task icollection_copy_to_throws_on_negative_index() {
        _sut.Add("a", 1);

        ICollection<KeyValuePair<string, int>> coll = _sut;

        var array = new KeyValuePair<string, int>[1];
        await Assert.That(() => coll.CopyTo(array, -1)).ThrowsExactly<IndexOutOfRangeException>();
    }

    [Test]
    public async Task icollection_copy_to_throws_on_insufficient_space() {
        _sut.Add("a", 1);
        _sut.Add("b", 2);

        ICollection<KeyValuePair<string, int>> coll = _sut;

        var array = new KeyValuePair<string, int>[3];

        await Assert.That(() => coll.CopyTo(array, 2)).ThrowsExactly<ArgumentException>(); // Only 1 space left, need 2
    }

    [Test]
    public async Task icollection_is_read_only_should_be_false() {
        ICollection<KeyValuePair<string, int>> coll = _sut;
        await Assert.That(coll.IsReadOnly).IsFalse();
    }

    [Test]
    public async Task concurrent_adds_should_maintain_consistency() {
        // Arrange
        var numTasks     = 20;
        var itemsPerTask = 1000;
        var tasks        = new List<Task>();
        // Use the shared SUT instance (_sut)

        // Act
        for (var i = 0; i < numTasks; i++) {
            var taskNum = i;
            tasks.Add(
                Task.Run(() => {
                        for (var j = 0; j < itemsPerTask; j++) {
                            var itemNum = taskNum * itemsPerTask + j;
                            var key     = $"K{itemNum}";
                            var value   = itemNum;
                            _sut.TryAdd(key, value); // Use TryAdd for concurrency
                        }
                    }
                )
            );
        }

        await Task.WhenAll(tasks);

        // Assert
        await Assert.That(_sut.Count).IsEqualTo(numTasks * itemsPerTask);

        // Verify bidirectional consistency
        for (var i = 0; i < numTasks * itemsPerTask; i++) {
            var key   = $"K{i}";
            var value = i;
            await Assert.That(_sut.ContainsKey(key)).IsTrue();
            await Assert.That(_sut.ContainsValue(value)).IsTrue();
            await Assert.That(_sut[key]).IsEqualTo(value);
            await Assert.That(_sut.GetKeyByValue(value)).IsEqualTo(key);
        }
    }

    [Test]
    public async Task concurrent_add_remove_update_should_maintain_consistency() {
        // Arrange
        var numWriterTasks    = 10;
        var operationsPerTask = 500;
        var numReaderTasks    = 5;
        var initialItems      = 100;
        var tasks             = new List<Task>();
        var rnd               = new Random();
        // Use shared _sut

        // Pre-populate
        for (var i = 0; i < initialItems; i++) _sut.TryAdd($"Initial{i}", i);

        // Act
        // Writer tasks (Add, Remove, Update via indexer)
        for (var i = 0; i < numWriterTasks; i++) {
            var taskNum = i;
            tasks.Add(
                Task.Run(() => {
                        for (var j = 0; j < operationsPerTask; j++) {
                            var opType  = rnd.Next(3);                // 0: Add, 1: Remove, 2: Update
                            var itemNum = rnd.Next(initialItems * 2); // Target items in a wider range

                            try {
                                // TryAdd
                                if (opType == 0) {
                                    _sut.TryAdd($"Task{taskNum}-{j}", itemNum);
                                }
                                // TryRemove (Key or Value)
                                else if (opType == 1) {
                                    if (rnd.Next(2) == 0)
                                        _sut.Remove($"SomeKey{itemNum}"); // Try removing potential keys
                                    else
                                        _sut.RemoveByValue(itemNum); // Try removing potential values
                                }
                                // Update/Set
                                else {
                                    _sut[$"UpdateKey{itemNum}"] = itemNum + 10000; // Set potentially new or existing keys
                                }
                            }
                            catch (ArgumentException) {
                                /* Expected for some concurrent sets/adds */
                            }
                            catch (KeyNotFoundException) {
                                /* Expected for some concurrent removes */
                            }
                        }
                    }
                )
            );
        }

        // Reader tasks
        for (var i = 0; i < numReaderTasks; i++) {
            tasks.Add(
                Task.Run(() => {
                        // More reads
                        for (var j = 0; j < operationsPerTask * 2; j++) {
                            var itemNum = rnd.Next(initialItems * 2);
                            _sut.TryGetValue($"SomeKey{itemNum}", out _);
                            _sut.TryGetKey(itemNum, out _);
                            _sut.ContainsKey($"AnotherKey{itemNum}");
                            _sut.ContainsValue(itemNum);
                            var currentCount = _sut.Count; // Access count
                            foreach (var _ in _sut) { }    // Try enumerating
                        }
                    }
                )
            );
        }

        await Task.WhenAll(tasks);

        // Assert
        // The exact count is non-deterministic, but the internal state MUST be consistent.
        await Assert.That(_sut.Count).IsGreaterThanOrEqualTo(0); // Sanity check count

        // *** The CRITICAL Assertion: Verify bidirectional consistency ***
        var forwardCopy = _sut.ToArray(); // Get a snapshot (uses GetEnumerator)

        await Assert.That(forwardCopy.Length).IsEqualTo(_sut.Count); // Snapshot count should match current count immediately after

        foreach (var (key, value) in forwardCopy) {
            // Check forward lookup consistency
            _sut.TryGetValue(key, out var retrievedValue);
            await Assert.That(retrievedValue).IsEqualTo(value);

            // Check reverse lookup consistency
            _sut.TryGetKey(value, out var retrievedKey);
            await Assert.That(retrievedKey).IsEqualTo(key);
        }

        // Also verify the reverse dictionary side implicitly agrees on count
        // (Cannot access _reverse directly, but the loop above verifies all _reverse entries)
        // We can explicitly check all _reverse entries via the Values collection if desired:
        var values = _sut.Values.ToList(); // Snapshot values
        await Assert.That(values.Count).IsEqualTo(_sut.Count);
        foreach (var val in values) {
            _sut.TryGetKey(val, out var key);
            await Assert.That(_sut.ContainsKey(key)).IsTrue(); // Key must still exist
            await Assert.That(_sut[key]).IsEqualTo(val);              // And map back to the value
        }
    }

    static KeyValuePair<TKey, TValue> Kvp<TKey, TValue>(TKey key, TValue value) => new(key, value);
}
