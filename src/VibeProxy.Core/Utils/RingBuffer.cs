using System.Collections;

namespace VibeProxy.Core.Utils;

public sealed class RingBuffer<T> : IEnumerable<T>
{
    private readonly T?[] _buffer;
    private int _head;
    private int _tail;
    private int _count;

    public RingBuffer(int capacity)
    {
        var safeCapacity = Math.Max(1, capacity);
        _buffer = new T?[safeCapacity];
    }

    public int Count => _count;

    public void Append(T item)
    {
        _buffer[_tail] = item;

        if (_count == _buffer.Length)
        {
            _head = (_head + 1) % _buffer.Length;
        }
        else
        {
            _count++;
        }

        _tail = (_tail + 1) % _buffer.Length;
    }

    public IReadOnlyList<T> ToList()
    {
        if (_count == 0)
        {
            return Array.Empty<T>();
        }

        var result = new List<T>(_count);
        for (var i = 0; i < _count; i++)
        {
            var index = (_head + i) % _buffer.Length;
            if (_buffer[index] is { } value)
            {
                result.Add(value);
            }
        }

        return result;
    }

    public IEnumerator<T> GetEnumerator() => ToList().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}