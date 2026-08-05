using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenEphys.MiniscopeV4.Gui.Tests;

[TestClass]
public class CircularBufferTests
{
    sealed class ReadOnlyList<T> : IReadOnlyList<T>
    {
        readonly T[] items;

        public ReadOnlyList(params T[] items)
        {
            this.items = items;
        }

        public T this[int index] => items[index];

        public int Count => items.Length;

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    struct Sample
    {
        public double Value;
    }

    /// <summary>
    /// Returns a sequence which does not implement <see cref="IReadOnlyList{T}"/>, so that
    /// pushing it falls back to pushing each element individually.
    /// </summary>
    static IEnumerable<int> Sequence(params int[] items)
    {
        foreach (var item in items)
            yield return item;
    }

    static int[] Range(int start, int count)
    {
        return Enumerable.Range(start, count).ToArray();
    }

    static CircularBuffer<int> CreateBuffer(int capacity, int itemCount = 0, int firstItem = 1)
    {
        var buffer = new CircularBuffer<int>(capacity);
        for (int i = 0; i < itemCount; i++)
            buffer.Push(firstItem + i);
        return buffer;
    }

    static int[] Snapshot(CircularBuffer<int> buffer, int capacity)
    {
        var data = new int[capacity];
        buffer.CopyTo(data);
        return data;
    }

    static void AssertBuffer(CircularBuffer<int> buffer, int count, int start, int end, params int[] data)
    {
        Assert.AreEqual(count, buffer.Count, "unexpected Count");
        Assert.AreEqual(start, buffer.Start, "unexpected Start");
        Assert.AreEqual(end, buffer.End, "unexpected End");
        CollectionAssert.AreEqual(data, Snapshot(buffer, data.Length), "unexpected buffer contents");
    }

    /// <summary>
    /// Asserts that pushing <paramref name="batches"/> as collections is indistinguishable from
    /// pushing every element individually, for each of the supported collection representations.
    /// </summary>
    static void AssertBatchPushMatchesSinglePush(int capacity, params int[][] batches)
    {
        var reference = new CircularBuffer<int>(capacity);
        foreach (var batch in batches)
            foreach (var item in batch)
                reference.Push(item);

        var expected = Snapshot(reference, capacity);
        var sources = new Dictionary<string, Func<int[], IReadOnlyList<int>>>
        {
            { "array", batch => batch },
            { "List", batch => new List<int>(batch) },
            { "readonly list", batch => new ReadOnlyList<int>(batch) }
        };

        foreach (var source in sources)
        {
            var actual = new CircularBuffer<int>(capacity);
            foreach (var batch in batches)
                actual.Push(source.Value(batch));

            var message = $"{source.Key} source, capacity {capacity}, batch sizes " +
                $"[{string.Join(",", batches.Select(batch => batch.Length))}]";
            Assert.AreEqual(reference.Count, actual.Count, $"unexpected Count for {message}");
            Assert.AreEqual(reference.Start, actual.Start, $"unexpected Start for {message}");
            Assert.AreEqual(reference.End, actual.End, $"unexpected End for {message}");
            CollectionAssert.AreEqual(expected, Snapshot(actual, capacity), $"unexpected contents for {message}");
        }
    }

    #region Constructor

    [TestMethod]
    public void Constructor_NewBuffer_IsEmpty()
    {
        var buffer = CreateBuffer(capacity: 4);
        AssertBuffer(buffer, count: 0, start: 0, end: 0, 0, 0, 0, 0);
        CollectionAssert.AreEqual(Array.Empty<int>(), buffer.ToArray());
    }

    [TestMethod]
    public void Constructor_ZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<int>(0));
    }

    [TestMethod]
    public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<int>(-1));
    }

    #endregion

    #region Push single item

    [TestMethod]
    public void PushItem_SingleItem_StoresItemAndAdvancesEnd()
    {
        var buffer = CreateBuffer(capacity: 4);
        buffer.Push(11);
        AssertBuffer(buffer, count: 1, start: 0, end: 1, 11, 0, 0, 0);
    }

    [TestMethod]
    public void PushItem_PartialFill_StartRemainsAtZero()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 3);
        AssertBuffer(buffer, count: 3, start: 0, end: 3, 1, 2, 3, 0);
    }

    [TestMethod]
    public void PushItem_ExactlyCapacity_EndWrapsToStart()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 4);
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 1, 2, 3, 4);
    }

    [TestMethod]
    public void PushItem_BeyondCapacity_OverwritesOldestItems()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 6);
        AssertBuffer(buffer, count: 4, start: 2, end: 2, 5, 6, 3, 4);
    }

    [TestMethod]
    public void PushItem_CapacityOfOne_AlwaysHoldsLatestItem()
    {
        var buffer = CreateBuffer(capacity: 1, itemCount: 3);
        AssertBuffer(buffer, count: 1, start: 0, end: 0, 3);
    }

    [TestMethod]
    public void PushItem_WhenBufferIsFull_StartTracksEnd()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 4);
        for (int i = 0; i < 8; i++)
        {
            buffer.Push(100 + i);
            Assert.AreEqual(buffer.End, buffer.Start);
            Assert.AreEqual(4, buffer.Count);
        }
    }

    #endregion

    #region Push list

    [TestMethod]
    public void PushList_EmptyList_DoesNotModifyBuffer()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 2);
        buffer.Push(Array.Empty<int>());
        AssertBuffer(buffer, count: 2, start: 0, end: 2, 1, 2, 0, 0);
    }

    [TestMethod]
    public void PushList_SmallerThanRemainingCapacity_AppendsItems()
    {
        var buffer = CreateBuffer(capacity: 5, itemCount: 2);
        buffer.Push(new[] { 11, 12 });
        AssertBuffer(buffer, count: 4, start: 0, end: 4, 1, 2, 11, 12, 0);
    }

    [TestMethod]
    public void PushList_ExactlyFillsRemainingCapacity_EndWrapsToStart()
    {
        var buffer = CreateBuffer(capacity: 5, itemCount: 2);
        buffer.Push(new[] { 11, 12, 13 });
        AssertBuffer(buffer, count: 5, start: 0, end: 0, 1, 2, 11, 12, 13);
    }

    [TestMethod]
    public void PushList_WrapsAroundEnd_SplitsItemsAcrossBufferBoundary()
    {
        var buffer = CreateBuffer(capacity: 5, itemCount: 3);
        buffer.Push(new[] { 11, 12, 13, 14 });
        AssertBuffer(buffer, count: 5, start: 2, end: 2, 13, 14, 3, 11, 12);
    }

    [TestMethod]
    public void PushList_ExactlyCapacity_ReplacesEntireBuffer()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 4);
        buffer.Push(new[] { 11, 12, 13, 14 });
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 11, 12, 13, 14);
    }

    [TestMethod]
    public void PushList_ExactlyCapacityAtNonZeroEnd_ReplacesEntireBuffer()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 2);
        buffer.Push(new[] { 11, 12, 13, 14 });
        AssertBuffer(buffer, count: 4, start: 2, end: 2, 13, 14, 11, 12);
    }

    [TestMethod]
    public void PushList_MultipleOfCapacity_KeepsOnlyFinalPass()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 4);
        buffer.Push(Range(11, 12));
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 19, 20, 21, 22);
    }

    [TestMethod]
    public void PushList_LargerThanCapacity_TrimsOldestItems()
    {
        var buffer = CreateBuffer(capacity: 4);
        buffer.Push(Range(1, 6));
        AssertBuffer(buffer, count: 4, start: 2, end: 2, 5, 6, 3, 4);
    }

    [TestMethod]
    public void PushList_LargerThanCapacityAtNonZeroEnd_TrimsOldestItems()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 2);
        buffer.Push(Range(11, 6));
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 13, 14, 15, 16);
    }

    [TestMethod]
    public void PushList_LargerThanCapacity_DoesNotLeaveStaleItems()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 4);
        buffer.Push(Range(11, 6));
        CollectionAssert.AreEquivalent(new[] { 13, 14, 15, 16 }, Snapshot(buffer, 4));
    }

    [TestMethod]
    public void PushList_ListSource_MatchesArraySource()
    {
        var buffer = CreateBuffer(capacity: 5, itemCount: 3);
        buffer.Push(new List<int> { 11, 12, 13, 14 });
        AssertBuffer(buffer, count: 5, start: 2, end: 2, 13, 14, 3, 11, 12);
    }

    [TestMethod]
    public void PushList_CustomListSource_MatchesArraySource()
    {
        var buffer = CreateBuffer(capacity: 5, itemCount: 3);
        buffer.Push(new ReadOnlyList<int>(11, 12, 13, 14));
        AssertBuffer(buffer, count: 5, start: 2, end: 2, 13, 14, 3, 11, 12);
    }

    [TestMethod]
    public void PushList_CustomListSourceLargerThanCapacity_TrimsOldestItems()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 2);
        buffer.Push(new ReadOnlyList<int>(Range(11, 6)));
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 13, 14, 15, 16);
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(8)]
    [DataRow(13)]
    public void PushList_AnyBatchSize_MatchesRepeatedSinglePush(int capacity)
    {
        for (int first = 0; first <= capacity * 2 + 1; first++)
            for (int second = 0; second <= capacity * 2 + 1; second++)
                AssertBatchPushMatchesSinglePush(capacity, Range(1, first), Range(101, second));
    }

    [DataTestMethod]
    [DataRow(3)]
    [DataRow(7)]
    public void PushList_ManyBatches_MatchesRepeatedSinglePush(int capacity)
    {
        var random = new Random(47);
        for (int trial = 0; trial < 200; trial++)
        {
            var batches = new int[random.Next(1, 6)][];
            var next = 1;
            for (int i = 0; i < batches.Length; i++)
            {
                batches[i] = Range(next, random.Next(0, capacity * 3 + 1));
                next += batches[i].Length;
            }

            AssertBatchPushMatchesSinglePush(capacity, batches);
        }
    }

    #endregion

    #region Push sequence

    [TestMethod]
    public void PushSequence_EmptySequence_DoesNotModifyBuffer()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 2);
        buffer.Push(Sequence());
        AssertBuffer(buffer, count: 2, start: 0, end: 2, 1, 2, 0, 0);
    }

    [TestMethod]
    public void PushSequence_NonListSource_PushesEachItem()
    {
        var buffer = CreateBuffer(capacity: 5, itemCount: 3);
        buffer.Push(Sequence(11, 12, 13, 14));
        AssertBuffer(buffer, count: 5, start: 2, end: 2, 13, 14, 3, 11, 12);
    }

    [TestMethod]
    public void PushSequence_NonListSourceLargerThanCapacity_KeepsNewestItems()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 2);
        buffer.Push(Sequence(Range(11, 6)));
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 13, 14, 15, 16);
    }

    [TestMethod]
    public void PushSequence_ListSource_MatchesListOverload()
    {
        var items = Range(11, 6);
        var expected = CreateBuffer(capacity: 4, itemCount: 2);
        expected.Push(items);

        var actual = CreateBuffer(capacity: 4, itemCount: 2);
        actual.Push((IEnumerable<int>)items);

        Assert.AreEqual(expected.Count, actual.Count);
        Assert.AreEqual(expected.Start, actual.Start);
        Assert.AreEqual(expected.End, actual.End);
        CollectionAssert.AreEqual(Snapshot(expected, 4), Snapshot(actual, 4));
    }

    #endregion

    #region Indexer

    [TestMethod]
    public void Indexer_PartiallyFilledBuffer_ReturnsItemsInSweepOrder()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 3);
        Assert.AreEqual(1, buffer[0]);
        Assert.AreEqual(2, buffer[1]);
        Assert.AreEqual(3, buffer[2]);
    }

    [TestMethod]
    public void Indexer_WrappedBuffer_ReturnsItemsInSweepOrder()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 6);
        Assert.AreEqual(5, buffer[0]);
        Assert.AreEqual(6, buffer[1]);
        Assert.AreEqual(3, buffer[2]);
        Assert.AreEqual(4, buffer[3]);
    }

    [TestMethod]
    public void Indexer_IndexBeyondCapacity_WrapsAround()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 4);
        Assert.AreEqual(buffer[0], buffer[4]);
        Assert.AreEqual(buffer[3], buffer[7]);
    }

    [TestMethod]
    public void Indexer_AssignedValue_ModifiesBufferInPlace()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 4);
        buffer[1] = 99;
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 1, 99, 3, 4);
    }

    [TestMethod]
    public void Indexer_StructElement_ReturnsMutableReference()
    {
        var buffer = new CircularBuffer<Sample>(2);
        buffer.Push(new Sample { Value = 1 });
        buffer.Push(new Sample { Value = 2 });

        ref var sample = ref buffer[0];
        sample.Value = 42;

        Assert.AreEqual(42.0, buffer[0].Value);
        Assert.AreEqual(2.0, buffer[1].Value);
    }

    #endregion

    #region CopyTo

    [TestMethod]
    public void CopyTo_PartiallyFilledBuffer_LeavesUnwrittenSlotsAtDefault()
    {
        var buffer = CreateBuffer(capacity: 5, itemCount: 2);
        var data = new int[5];
        buffer.CopyTo(data);
        CollectionAssert.AreEqual(new[] { 1, 2, 0, 0, 0 }, data);
    }

    [TestMethod]
    public void CopyTo_WrappedBuffer_CopiesInSweepOrderNotChronologicalOrder()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 6);
        var data = new int[4];
        buffer.CopyTo(data);
        CollectionAssert.AreEqual(new[] { 5, 6, 3, 4 }, data);
    }

    #endregion

    #region Enumeration

    [TestMethod]
    public void GetEnumerator_EmptyBuffer_YieldsNothing()
    {
        var buffer = CreateBuffer(capacity: 4);
        CollectionAssert.AreEqual(Array.Empty<int>(), buffer.ToArray());
    }

    [TestMethod]
    public void GetEnumerator_PartiallyFilledBuffer_YieldsOnlyPushedItems()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 3);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, buffer.ToArray());
    }

    [TestMethod]
    public void GetEnumerator_WrappedBuffer_YieldsItemsInSweepOrder()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 6);
        CollectionAssert.AreEqual(new[] { 5, 6, 3, 4 }, buffer.ToArray());
    }

    [TestMethod]
    public void GetEnumerator_NonGeneric_MatchesGenericEnumerator()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 6);
        var items = new List<int>();
        foreach (int item in (IEnumerable)buffer)
            items.Add(item);

        CollectionAssert.AreEqual(buffer.ToArray(), items);
    }

    #endregion

    #region Clone

    [TestMethod]
    public void Clone_PartiallyFilledBuffer_CopiesState()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 3);
        var clone = buffer.Clone();
        AssertBuffer(clone, count: 3, start: 0, end: 3, 1, 2, 3, 0);
    }

    [TestMethod]
    public void Clone_WrappedBuffer_PreservesSweepPosition()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 6);
        var clone = buffer.Clone();
        AssertBuffer(clone, count: 4, start: 2, end: 2, 5, 6, 3, 4);
    }

    [TestMethod]
    public void Clone_PushToClone_DoesNotModifyOriginal()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 3);
        var clone = buffer.Clone();
        clone.Push(99);

        AssertBuffer(buffer, count: 3, start: 0, end: 3, 1, 2, 3, 0);
        AssertBuffer(clone, count: 4, start: 0, end: 0, 1, 2, 3, 99);
    }

    [TestMethod]
    public void Clone_PushToOriginal_DoesNotModifyClone()
    {
        var buffer = CreateBuffer(capacity: 4, itemCount: 3);
        var clone = buffer.Clone();
        buffer.Push(99);

        AssertBuffer(clone, count: 3, start: 0, end: 3, 1, 2, 3, 0);
        AssertBuffer(buffer, count: 4, start: 0, end: 0, 1, 2, 3, 99);
    }

    #endregion
}
