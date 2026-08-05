using System;
using System.Collections;
using System.Collections.Generic;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// A circular buffer implementation that allows for efficient storage and retrieval of elements
/// in a fixed-size buffer. When the buffer is full, new elements overwrite the oldest elements.
/// </summary>
/// <remarks>
/// Data is not returned in chronological order, but is returned in the order of the underlying array.
/// This creates an oscilloscope-style sweep when visualizing the data.
/// </remarks>
/// <typeparam name="T"></typeparam>
class CircularBuffer<T> : IReadOnlyCollection<T>
{
    private readonly T[] buffer;

    public CircularBuffer(int capacity)
    {
        buffer = new T[capacity];
    }

    CircularBuffer(CircularBuffer<T> other)
        : this(other.buffer.Length)
    {
        Array.Copy(other.buffer, buffer, other.buffer.Length);
        start = other.start;
        end = other.end;
        count = other.count;
    }

    internal CircularBuffer<T> Clone()
    {
        var clone = new CircularBuffer<T>(this);
        return clone;
    }

    private int start;
    private int end;
    private int count;

    public ref T this[int index]
    {
        get => ref buffer[index % buffer.Length];
    }

    public int Count => count;

    public int Start => start;

    public int End => end;

    public void Push(T item)
    {
        buffer[end] = item;
        end = (end + 1) % buffer.Length;
        if (count == buffer.Length)
            start = (start + 1) % buffer.Length;
        else
            count++;
    }

    static void CopyList<TSource>(IReadOnlyList<TSource> source, int index, TSource[] buffer, int bufferIndex, int count)
    {
        if (source is TSource[] array)
            Array.Copy(array, index, buffer, bufferIndex, count);
        else if (source is List<TSource> list)
            list.CopyTo(index, buffer, bufferIndex, count);
        else
            for (int i = index, bi = bufferIndex; i < index + count; i++, bi++)
                buffer[bi] = source[i];
    }

    public void Push(IReadOnlyList<T> items)
    {
        var trimOffset = Math.Max(0, items.Count - buffer.Length);
        var pushCount = items.Count - trimOffset;
        start = (start + items.Count - Math.Min(items.Count, buffer.Length - count)) % buffer.Length;
        count = Math.Min(count + items.Count, buffer.Length);

        var index = (end + trimOffset) % buffer.Length;
        end = (index + pushCount) % buffer.Length;

        var segmentLength = Math.Min(pushCount, buffer.Length - index);
        CopyList(items, trimOffset, buffer, index, segmentLength);
        if (segmentLength < pushCount)
            CopyList(items, trimOffset + segmentLength, buffer, 0, pushCount - segmentLength);
    }

    public void Push(IEnumerable<T> items)
    {
        if (items is IReadOnlyList<T> list)
            Push(list);
        else
            foreach (var item in items)
                Push(item);
    }

    public void CopyTo(T[] array) => Array.Copy(buffer, array, buffer.Length);

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
