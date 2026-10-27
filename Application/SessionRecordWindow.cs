namespace SerialBench.Application;

/// <summary>
/// Maintains a bounded, contiguous projection window over session records. The window may grow
/// freely through the configured comfortable range; it fetches near an edge and trims only after
/// exceeding the maximum working-set size.
/// </summary>
public sealed class SessionRecordWindow<T>
{
    private readonly Func<int, T> loadRecord;
    private readonly List<T> records = [];

    /// <summary>Creates a window whose projection records are loaded by zero-based ordinal.</summary>
    public SessionRecordWindow(Func<int, T> loadRecord) =>
        this.loadRecord = loadRecord ?? throw new ArgumentNullException(nameof(loadRecord));

    /// <summary>Gets the projection ordinal of the first staged record.</summary>
    public int StartOrdinal { get; private set; }

    /// <summary>Gets the currently staged records.</summary>
    public IReadOnlyList<T> Records => records;

    /// <summary>Gets the projection ordinal immediately after the staged range.</summary>
    public int EndOrdinal => StartOrdinal + records.Count;

    /// <summary>Clears the staged window. A valid presentation window is allowed to contain zero records.</summary>
    public void Clear()
    {
        records.Clear();
        StartOrdinal = 0;
    }

    /// <summary>
    /// Ensures that the inclusive visible projection range is staged with useful clearance on both
    /// sides when history exists. Existing overlap is retained; only newly required records load.
    /// </summary>
    public void EnsureVisibleRange(int firstVisibleOrdinal, int lastVisibleOrdinal, int totalRecords)
    {
        ValidateRange(firstVisibleOrdinal, lastVisibleOrdinal, totalRecords);
        if (totalRecords == 0) { Clear(); return; }

        if (records.Count == 0 || firstVisibleOrdinal < StartOrdinal || lastVisibleOrdinal >= EndOrdinal)
        {
            LoadCentered(firstVisibleOrdinal, lastVisibleOrdinal, totalRecords);
            return;
        }

        if (firstVisibleOrdinal - StartOrdinal < SessionPresentationLimits.EdgeClearanceRecords && StartOrdinal > 0)
            ExtendBefore(Math.Min(SessionPresentationLimits.FetchRecords, StartOrdinal));

        if (EndOrdinal - 1 - lastVisibleOrdinal < SessionPresentationLimits.EdgeClearanceRecords && EndOrdinal < totalRecords)
            ExtendAfter(Math.Min(SessionPresentationLimits.FetchRecords, totalRecords - EndOrdinal));

        if (records.Count > SessionPresentationLimits.MaximumRecords)
            TrimAround(firstVisibleOrdinal, lastVisibleOrdinal, totalRecords);
    }

    /// <summary>
    /// Appends a newly available projection record when this window currently reaches the previous
    /// end of the projection. The window is allowed to grow until its maximum before trimming.
    /// </summary>
    public bool TryAppendTail(int ordinal, T record)
    {
        if (records.Count == 0)
        {
            if (ordinal != 0) return false;
            StartOrdinal = 0;
            records.Add(record);
            return true;
        }
        if (ordinal != EndOrdinal) return false;
        records.Add(record);
        return true;
    }

    private void LoadCentered(int firstVisibleOrdinal, int lastVisibleOrdinal, int totalRecords)
    {
        var visibleCount = lastVisibleOrdinal - firstVisibleOrdinal + 1;
        var desired = Math.Max(SessionPresentationLimits.NominalRecords, visibleCount);
        desired = Math.Min(desired, totalRecords);
        var clearance = Math.Max(0, (desired - visibleCount) / 2);
        var start = Math.Max(0, firstVisibleOrdinal - clearance);
        if (start + desired > totalRecords) start = Math.Max(0, totalRecords - desired);
        Replace(start, desired);
    }

    private void ExtendBefore(int count)
    {
        if (count <= 0) return;
        var start = StartOrdinal - count;
        var added = new T[count];
        for (var i = 0; i < count; i++) added[i] = loadRecord(start + i);
        records.InsertRange(0, added);
        StartOrdinal = start;
    }

    private void ExtendAfter(int count)
    {
        for (var i = 0; i < count; i++) records.Add(loadRecord(EndOrdinal));
    }

    private void TrimAround(int firstVisibleOrdinal, int lastVisibleOrdinal, int totalRecords)
    {
        var visibleCount = lastVisibleOrdinal - firstVisibleOrdinal + 1;
        var desired = Math.Max(SessionPresentationLimits.NominalRecords, visibleCount);
        desired = Math.Min(desired, totalRecords);
        var clearance = Math.Max(0, (desired - visibleCount) / 2);
        var desiredStart = Math.Max(0, firstVisibleOrdinal - clearance);
        if (desiredStart + desired > totalRecords) desiredStart = Math.Max(0, totalRecords - desired);

        var removeBefore = desiredStart - StartOrdinal;
        if (removeBefore > 0)
        {
            records.RemoveRange(0, removeBefore);
            StartOrdinal += removeBefore;
        }
        var desiredEnd = desiredStart + desired;
        if (EndOrdinal > desiredEnd) records.RemoveRange(desiredEnd - StartOrdinal, EndOrdinal - desiredEnd);
    }

    private void Replace(int start, int count)
    {
        records.Clear();
        StartOrdinal = start;
        for (var i = 0; i < count; i++) records.Add(loadRecord(start + i));
    }

    private static void ValidateRange(int first, int last, int total)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (total == 0)
        {
            if (first != 0 || last != -1) throw new ArgumentOutOfRangeException(nameof(first));
            return;
        }
        if (first < 0 || last < first || last >= total) throw new ArgumentOutOfRangeException(nameof(first));
    }
}
