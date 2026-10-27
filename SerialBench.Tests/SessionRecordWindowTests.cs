using SerialBench.Application;

namespace SerialBench.Tests;

public sealed class SessionRecordWindowTests
{
    [Fact]
    public void EmptyProjectionProducesEmptyWindow()
    {
        var window = new SessionRecordWindow<int>(i => i);
        window.EnsureVisibleRange(0, -1, 0);
        Assert.Empty(window.Records);
        Assert.Equal(0, window.StartOrdinal);
    }

    [Fact]
    public void InitialLoadCentersVisibleRangeWithoutReadingWholeProjection()
    {
        var loaded = new List<int>();
        var window = new SessionRecordWindow<int>(i => { loaded.Add(i); return i; });
        window.EnsureVisibleRange(5000, 5019, 10000);
        Assert.Contains(5000, window.Records);
        Assert.Contains(5019, window.Records);
        Assert.True(window.StartOrdinal < 5000);
        Assert.True(window.EndOrdinal > 5019);
        Assert.True(window.Records.Count < 1000);
        Assert.Equal(window.Records.Count, loaded.Count);
    }

    [Fact]
    public void ApproachingEdgePreservesOverlapAndLoadsOnlyNewBlock()
    {
        var loadCount = 0;
        var window = new SessionRecordWindow<int>(i => { loadCount++; return i; });
        window.EnsureVisibleRange(5000, 5019, 10000);
        var initial = window.Records.ToArray();
        var initialLoads = loadCount;
        var nearTop = window.StartOrdinal + 10;
        window.EnsureVisibleRange(nearTop, nearTop + 19, 10000);
        Assert.True(window.StartOrdinal < initial[0]);
        Assert.True(loadCount - initialLoads < initialLoads);
        Assert.Contains(initial[0], window.Records);
    }

    [Fact]
    public void TailCanGrowWithoutRebuildingWindow()
    {
        var window = new SessionRecordWindow<int>(i => i);
        for (var i = 0; i < 400; i++) Assert.True(window.TryAppendTail(i, i));
        Assert.Equal(400, window.Records.Count);
        Assert.Equal(0, window.StartOrdinal);
        Assert.Equal(400, window.EndOrdinal);
        Assert.Equal(399, window.Records[^1]);
    }

    [Fact]
    public void RecenterNearSessionBoundaryBiasesTowardBoundary()
    {
        var window = new SessionRecordWindow<int>(i => i);
        window.EnsureVisibleRange(9950, 9999, 10000);
        Assert.Equal(10000, window.EndOrdinal);
        Assert.Contains(9950, window.Records);
        Assert.Contains(9999, window.Records);
    }
}
