namespace SerialBench.Application;

/// <summary>Bounded Received projection over the Rx-message index of a session store.</summary>
public sealed class ReceivedSessionProjection
{
    private readonly SessionStore store;
    private readonly SessionRecordWindow<RxSessionRecord> window;

    public ReceivedSessionProjection(SessionStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        window = new SessionRecordWindow<RxSessionRecord>(store.ReadRxMessage);
    }

    public int Count => store.RxMessageCount;
    public int StartOrdinal => window.StartOrdinal;
    public int EndOrdinal => window.EndOrdinal;
    public IReadOnlyList<RxSessionRecord> Records => window.Records;

    public void EnsureVisibleRange(int firstVisibleOrdinal, int lastVisibleOrdinal) =>
        window.EnsureVisibleRange(firstVisibleOrdinal, lastVisibleOrdinal, Count);

    public void ShowTail(int visibleRecordCount)
    {
        if (visibleRecordCount < 0) throw new ArgumentOutOfRangeException(nameof(visibleRecordCount));
        if (Count == 0) { window.Clear(); return; }
        var last = Count - 1;
        var first = Math.Max(0, Count - Math.Max(1, visibleRecordCount));
        window.EnsureVisibleRange(first, last, Count);
    }

    /// <summary>Incorporates a newly appended Rx record without rereading it when the staged window reaches the old tail.</summary>
    public bool TryAppendTail(int rxOrdinal, RxSessionRecord record) => window.TryAppendTail(rxOrdinal, record);

    public string Render(ReceivedDataFormat format) => SessionTextRenderer.RenderReceivedWindow(window.Records, format);
}

/// <summary>Bounded chronological Trace projection over every event in a session store.</summary>
public sealed class TraceSessionProjection
{
    private readonly SessionStore store;
    private readonly SessionRecordWindow<SessionRecord> window;

    public TraceSessionProjection(SessionStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        window = new SessionRecordWindow<SessionRecord>(store.ReadEvent);
    }

    public int Count => store.EventCount;
    public int StartOrdinal => window.StartOrdinal;
    public int EndOrdinal => window.EndOrdinal;
    public IReadOnlyList<SessionRecord> Records => window.Records;

    public void EnsureVisibleRange(int firstVisibleOrdinal, int lastVisibleOrdinal) =>
        window.EnsureVisibleRange(firstVisibleOrdinal, lastVisibleOrdinal, Count);

    public void ShowTail(int visibleRecordCount)
    {
        if (visibleRecordCount < 0) throw new ArgumentOutOfRangeException(nameof(visibleRecordCount));
        if (Count == 0) { window.Clear(); return; }
        var last = Count - 1;
        var first = Math.Max(0, Count - Math.Max(1, visibleRecordCount));
        window.EnsureVisibleRange(first, last, Count);
    }

    /// <summary>Incorporates a newly appended event without rereading it when the staged window reaches the old tail.</summary>
    public bool TryAppendTail(int eventOrdinal, SessionRecord record) => window.TryAppendTail(eventOrdinal, record);

    public string Render() => SessionTextRenderer.RenderTraceWindow(window.Records);
}
