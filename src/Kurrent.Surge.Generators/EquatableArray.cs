using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Kurrent.Surge.Codecs.Avro.Generators;

/// <summary>
/// An array with element-wise equality, for use as an incremental generator pipeline value.
/// </summary>
/// <remarks>
/// <see cref="System.Collections.Immutable.ImmutableArray{T}"/> compares by the identity of its
/// underlying array, so a model holding one is never equal to a structurally identical model. That
/// defeats the pipeline cache and makes the generator re-run on every keystroke.
/// </remarks>
readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T> {
    readonly T[] _items = items;

    public static readonly EquatableArray<T> Empty = new([]);

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other) => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode() {
        if (_items is null) return 0;

        var hash = 17;

        foreach (var item in _items) hash = hash * 31 + (item?.GetHashCode() ?? 0);

        return hash;
    }

    ReadOnlySpan<T> AsSpan() => _items ?? [];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
