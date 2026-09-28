namespace CnCNew.Simulation.World;

/// <summary>
/// Binary min-heap whose items know their own position, so an item can be removed or re-sifted in O(log n) — the same
/// structure the reference uses for its sleepy updates (<c>m_sleepyUpdates</c>, <c>indexInLogic</c>). The comparison must be
/// a strict total order (no ties) so that the pop order never depends on heap internals.
/// </summary>
internal sealed class IndexedHeap<T> where T : class
{
    private readonly List<T> _items = [];
    private readonly Comparison<T> _compare;
    private readonly Func<T, int> _getIndex;
    private readonly Action<T, int> _setIndex;

    public IndexedHeap(Comparison<T> compare, Func<T, int> getIndex, Action<T, int> setIndex)
    {
        _compare = compare;
        _getIndex = getIndex;
        _setIndex = setIndex;
    }

    public int Count => _items.Count;
    public T Peek() => _items[0];
    public bool Contains(T item) => _getIndex(item) >= 0;

    public void Push(T item)
    {
        if (_getIndex(item) >= 0) throw new InvalidOperationException("Item is already in the heap.");
        _items.Add(item);
        _setIndex(item, _items.Count - 1);
        SiftUp(_items.Count - 1);
    }

    public T Pop()
    {
        var top = _items[0];
        RemoveAt(0);
        return top;
    }

    public bool Remove(T item)
    {
        var index = _getIndex(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public void Update(T item)
    {
        var index = _getIndex(item);
        if (index < 0) throw new InvalidOperationException("Item is not in the heap.");
        SiftUp(index);
        SiftDown(_getIndex(item));
    }

    public void Clear()
    {
        foreach (var item in _items) _setIndex(item, -1);
        _items.Clear();
    }

    /// <summary>Items in unspecified order; only for verification and snapshot building (callers sort).</summary>
    public IReadOnlyList<T> Items => _items;

    private void RemoveAt(int index)
    {
        var item = _items[index];
        var last = _items.Count - 1;
        T? moved = null;
        if (index != last)
        {
            moved = _items[last];
            _items[index] = moved;
            _setIndex(moved, index);
        }
        _items.RemoveAt(last);
        _setIndex(item, -1);
        if (moved is not null)
        {
            SiftUp(index);
            SiftDown(_getIndex(moved));
        }
    }

    private void SiftUp(int index)
    {
        var item = _items[index];
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            if (_compare(item, _items[parent]) >= 0) break;
            _items[index] = _items[parent];
            _setIndex(_items[index], index);
            index = parent;
        }
        _items[index] = item;
        _setIndex(item, index);
    }

    private void SiftDown(int index)
    {
        var item = _items[index];
        var count = _items.Count;
        while (true)
        {
            var child = (index << 1) + 1;
            if (child >= count) break;
            if (child + 1 < count && _compare(_items[child + 1], _items[child]) < 0) child++;
            if (_compare(_items[child], item) >= 0) break;
            _items[index] = _items[child];
            _setIndex(_items[index], index);
            index = child;
        }
        _items[index] = item;
        _setIndex(item, index);
    }
}
