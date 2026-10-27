using SerialBench.Application;

namespace SerialBench.Tests;

public sealed class SessionProjectionTests
{
    [Fact]
    public void ReceivedProjectionUsesRxIndexRatherThanAllEvents()
    {
        var path = TempPath();
        try
        {
            using var store = new SessionStore(path);
            for (var i = 0; i < 20; i++)
            {
                store.AppendDiagnostic(DateTime.UnixEpoch.AddTicks(i * 2), $"d{i}");
                store.AppendRxMessage(DateTime.UnixEpoch.AddTicks(i * 2 + 1), SessionCheckValidity.Ignore, [], [(byte)i], []);
            }

            var projection = new ReceivedSessionProjection(store);
            projection.ShowTail(5);

            Assert.Equal(20, projection.Count);
            Assert.Equal(20, projection.EndOrdinal);
            Assert.Equal((byte)19, projection.Records[^1].Payload[0]);
            Assert.DoesNotContain("d19", projection.Render(ReceivedDataFormat.Bytes));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TraceProjectionIncludesEveryEventInOrder()
    {
        var path = TempPath();
        try
        {
            using var store = new SessionStore(path);
            store.AppendDiagnostic(DateTime.UnixEpoch, "first");
            store.AppendTxMessage(DateTime.UnixEpoch.AddTicks(1), [], [0x41], []);
            store.AppendRxMessage(DateTime.UnixEpoch.AddTicks(2), SessionCheckValidity.Valid, [], [0x42], []);

            var projection = new TraceSessionProjection(store);
            projection.ShowTail(3);

            Assert.Equal(3, projection.Count);
            Assert.Collection(projection.Records,
                record => Assert.IsType<DiagnosticSessionRecord>(record),
                record => Assert.IsType<TxSessionRecord>(record),
                record => Assert.IsType<RxSessionRecord>(record));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LiveTailAppendAvoidsStoreReread()
    {
        var path = TempPath();
        try
        {
            using var store = new SessionStore(path);
            var projection = new TraceSessionProjection(store);
            var record = new DiagnosticSessionRecord(DateTime.UnixEpoch, "live");
            var ordinal = store.AppendDiagnostic(record.TimestampUtc, record.Text);

            Assert.True(projection.TryAppendTail(ordinal, record));
            Assert.Single(projection.Records);
            Assert.Contains("live", projection.Render());
        }
        finally { File.Delete(path); }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"SerialBench-{Guid.NewGuid():N}.sbss");
}
