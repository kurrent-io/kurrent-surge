namespace Kurrent.Client.Tests.Model;

public class MetadataTests {
    [Test]
    public void metadata_can_be_locked() {
        var metadata = new Metadata();
        metadata.IsLocked.ShouldBeFalse();

        var locked = metadata.Lock();
        locked.IsLocked.ShouldBeTrue();
        locked.ShouldBeSameAs(metadata);
    }

    [Test]
    public void locked_metadata_throws_on_indexer_set() {
        var metadata = new Metadata().Lock();

        var exception = Should.Throw<InvalidOperationException>(() => metadata["key"] = "value");
        exception.Message.ShouldBe("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public void locked_metadata_throws_on_remove() {
        var metadata = new Metadata().With("key", "value").Lock();

        var exception = Should.Throw<InvalidOperationException>(() => metadata.Remove("key"));
        exception.Message.ShouldBe("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public void locked_metadata_with_throws_exception() {
        var locked = new Metadata().With("original", "value").Lock();

        var exception = Should.Throw<InvalidOperationException>(() => locked.With("new", "value"));
        exception.Message.ShouldBe("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public void locked_metadata_with_generic_throws_exception() {
        var locked = new Metadata().With("original", 123).Lock();

        var exception = Should.Throw<InvalidOperationException>(() => locked.With<int>("new", 456));
        exception.Message.ShouldBe("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public void locked_metadata_with_many_throws_exception() {
        var locked     = new Metadata().With("original", "value").Lock();
        var additional = new Metadata().With("new1", "value1").With("new2", "value2");

        var exception = Should.Throw<InvalidOperationException>(() => locked.WithMany(additional));
        exception.Message.ShouldBe("Cannot modify locked metadata. Use CreateUnlockedCopy() to create a mutable copy.");
    }

    [Test]
    public void create_unlocked_copy_returns_unlocked_copy() {
        var locked = new Metadata().With("key", "value").Lock();

        var unlocked = locked.CreateUnlockedCopy();

        unlocked.ShouldNotBeSameAs(locked);
        unlocked.IsLocked.ShouldBeFalse();
        locked.IsLocked.ShouldBeTrue();
        unlocked.ContainsKey("key").ShouldBeTrue();
        unlocked["key"].ShouldBe("value");
    }

    [Test]
    public void create_unlocked_copy_from_unlocked_returns_unlocked() {
        var unlocked = new Metadata().With("key", "value");

        var copy = unlocked.CreateUnlockedCopy();

        copy.ShouldNotBeSameAs(unlocked);
        copy.IsLocked.ShouldBeFalse();
        unlocked.IsLocked.ShouldBeFalse();
    }

    [Test]
    public void unlocked_copy_can_be_modified() {
        var locked = new Metadata().With("original", "value").Lock();

        var unlocked = locked.CreateUnlockedCopy();
        var modified = unlocked.With("new", "value");

        modified.ShouldBeSameAs(unlocked);
        modified.Count.ShouldBe(2);
        locked.Count.ShouldBe(1);
    }

    [Test]
    public void unlocked_metadata_with_modifies_self() {
        var metadata = new Metadata().With("original", "value");

        var result = metadata.With("new", "value");

        result.ShouldBeSameAs(metadata);
        result.IsLocked.ShouldBeFalse();
        result.Count.ShouldBe(2);
    }

    [Test]
    public void can_lock_after_create_unlocked_copy() {
        var locked = new Metadata().With("original", "value").Lock();
        var copy   = locked.CreateUnlockedCopy();

        var newLocked = copy.Lock();

        newLocked.ShouldBeSameAs(copy);
        newLocked.IsLocked.ShouldBeTrue();

        Should.Throw<InvalidOperationException>(() => newLocked["another"] = "value");
    }

    [Test]
    public void indexer_get_works_on_locked_metadata() {
        var metadata = new Metadata().With("key", "value").Lock();

        var value = metadata["key"];

        value.ShouldBe("value");
    }

    [Test]
    public void enumeration_works_on_locked_metadata() {
        var metadata = new Metadata()
            .With("key1", "value1")
            .With("key2", "value2")
            .Lock();

        var items = metadata.ToList();

        items.Count.ShouldBe(2);
        items.ShouldContain(kvp => kvp.Key == "key1" && kvp.Value!.ToString() == "value1");
        items.ShouldContain(kvp => kvp.Key == "key2" && kvp.Value!.ToString() == "value2");
    }

    [Test]
    public void readonly_operations_work_on_locked_metadata() {
        var metadata = new Metadata()
            .With("string", "value")
            .With("int", 42)
            .Lock();

        metadata.Count.ShouldBe(2);
        metadata.ContainsKey("string").ShouldBeTrue();
        metadata.ContainsKey("missing").ShouldBeFalse();
        metadata.GetRequired<string>("string").ShouldBe("value");
        metadata.GetRequired<int>("int").ShouldBe(42);
        metadata.GetOrDefault<string>("missing", "default").ShouldBe("default");
        metadata.TryGet<string>("string", out var value).ShouldBeTrue();
        value.ShouldBe("value");
    }
}
