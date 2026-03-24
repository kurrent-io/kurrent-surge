namespace Kurrent.Client.Tests.Model;

public class MetadataTests {
    [Test]
    public async Task metadata_can_be_locked() {
        var metadata = new Metadata();
        await Assert.That(metadata.IsLocked).IsFalse();

        var locked = metadata.Lock();
        await Assert.That(locked.IsLocked).IsTrue();
        await Assert.That(locked).IsSameReferenceAs(metadata);
    }

    [Test]
    public async Task locked_metadata_throws_on_indexer_set() {
        var metadata = new Metadata().Lock();

        var exception = await Assert.That(() => metadata["key"] = "value").ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).IsEqualTo("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public async Task locked_metadata_throws_on_remove() {
        var metadata = new Metadata().With("key", "value").Lock();

        var exception = await Assert.That(() => metadata.Remove("key")).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).IsEqualTo("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public async Task locked_metadata_with_throws_exception() {
        var locked = new Metadata().With("original", "value").Lock();

        var exception = await Assert.That(() => locked.With("new", "value")).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).IsEqualTo("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public async Task locked_metadata_with_generic_throws_exception() {
        var locked = new Metadata().With("original", 123).Lock();

        var exception = await Assert.That(() => locked.With<int>("new", 456)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).IsEqualTo("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public async Task locked_metadata_with_many_throws_exception() {
        var locked     = new Metadata().With("original", "value").Lock();
        var additional = new Metadata().With("new1", "value1").With("new2", "value2");

        var exception = await Assert.That(() => locked.WithMany(additional)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).IsEqualTo("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public async Task create_unlocked_copy_returns_unlocked_copy() {
        var locked = new Metadata().With("key", "value").Lock();

        var unlocked = locked.CreateUnlockedCopy();

        await Assert.That(unlocked).IsNotSameReferenceAs(locked);
        await Assert.That(unlocked.IsLocked).IsFalse();
        await Assert.That(locked.IsLocked).IsTrue();
        await Assert.That(unlocked.ContainsKey("key")).IsTrue();
        await Assert.That(unlocked["key"]).IsEqualTo("value");
    }

    [Test]
    public async Task create_unlocked_copy_from_unlocked_returns_unlocked() {
        var unlocked = new Metadata().With("key", "value");

        var copy = unlocked.CreateUnlockedCopy();

        await Assert.That(copy).IsNotSameReferenceAs(unlocked);
        await Assert.That(copy.IsLocked).IsFalse();
        await Assert.That(unlocked.IsLocked).IsFalse();
    }

    [Test]
    public async Task unlocked_copy_can_be_modified() {
        var locked = new Metadata().With("original", "value").Lock();

        var unlocked = locked.CreateUnlockedCopy();
        var modified = unlocked.With("new", "value");

        await Assert.That(modified).IsSameReferenceAs(unlocked);
        await Assert.That(modified.Count).IsEqualTo(2);
        await Assert.That(locked.Count).IsEqualTo(1);
    }

    [Test]
    public async Task unlocked_metadata_with_modifies_self() {
        var metadata = new Metadata().With("original", "value");

        var result = metadata.With("new", "value");

        await Assert.That(result).IsSameReferenceAs(metadata);
        await Assert.That(result.IsLocked).IsFalse();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task can_lock_after_create_unlocked_copy() {
        var locked = new Metadata().With("original", "value").Lock();
        var copy   = locked.CreateUnlockedCopy();

        var newLocked = copy.Lock();

        await Assert.That(newLocked).IsSameReferenceAs(copy);
        await Assert.That(newLocked.IsLocked).IsTrue();

        await Assert.That(() => newLocked["another"] = "value").ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task indexer_get_works_on_locked_metadata() {
        var metadata = new Metadata().With("key", "value").Lock();

        var value = metadata["key"];

        await Assert.That(value).IsEqualTo("value");
    }

    [Test]
    public async Task enumeration_works_on_locked_metadata() {
        var metadata = new Metadata()
            .With("key1", "value1")
            .With("key2", "value2")
            .Lock();

        var items = metadata.ToList();

        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(items).Contains(kvp => kvp.Key == "key1" && kvp.Value!.ToString() == "value1");
        await Assert.That(items).Contains(kvp => kvp.Key == "key2" && kvp.Value!.ToString() == "value2");
    }

    [Test]
    public async Task readonly_operations_work_on_locked_metadata() {
        var metadata = new Metadata()
            .With("string", "value")
            .With("int", 42)
            .Lock();

        await Assert.That(metadata.Count).IsEqualTo(2);
        await Assert.That(metadata.ContainsKey("string")).IsTrue();
        await Assert.That(metadata.ContainsKey("missing")).IsFalse();
        await Assert.That(metadata.GetRequiredValue<string>("string")).IsEqualTo("value");
        await Assert.That(metadata.GetRequiredValue<int>("int")).IsEqualTo(42);
        await Assert.That(metadata.GetValueOrDefault<string>("missing", "default")).IsEqualTo("default");
        await Assert.That(metadata.TryGetValue<string>("string", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("value");
    }
}
