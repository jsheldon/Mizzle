using System.Collections;
using System.Runtime.CompilerServices;

namespace Mizzle.Ir;

[CollectionBuilder(typeof(EquatableList), nameof(EquatableList.Create))]
public sealed class EquatableList<T> : IReadOnlyList<T>, IEquatable<EquatableList<T>>
{
    private readonly T[] _items;

    public EquatableList(IEnumerable<T> items) => _items = [..items];

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public bool Equals(EquatableList<T>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_items.Length != other._items.Length)
        {
            return false;
        }

        for (var i = 0; i < _items.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(_items[i], other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as EquatableList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_items.Length);
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

public static class EquatableList
{
    public static EquatableList<T> Create<T>(ReadOnlySpan<T> items) => new(items.ToArray());
}
