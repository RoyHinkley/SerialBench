using System.Text;
using SerialBench.Application;

namespace SerialBench.Tests;

public sealed class SessionStoreTests
{
    [Fact]
    public void WritesVersionedHeaderAndRoundTripsAllRecordTypes()
    {
        var path = TempPath();
        try
        {
            var txTime = new DateTime(638900000000001234, DateTimeKind.Utc);
            var rxTime = txTime.AddTicks(17);
            var diagnosticTime = txTime.AddTicks(29);

            using (var store = new SessionStore(path))
            {
                Assert.Equal(0, store.AppendTxMessage(txTime, [0x02], [0x10, 0x20], [0x03]));
                Assert.Equal(1, store.AppendRxMessage(rxTime, SessionCheckValidity.Invalid, [0xAA, 0xBB], [0x30], [0xCC, 0xDD]));
                Assert.Equal(2, store.AppendDiagnostic(diagnosticTime, "check failed: µ"));
                Assert.Equal(3, store.EventCount);
                Assert.Equal([1], store.RxMessageEvents);

                var tx = Assert.IsType<TxSessionRecord>(store.ReadEvent(0));
                Assert.Equal(txTime, tx.TimestampUtc);
                Assert.Equal([0x02], tx.Prefix);
                Assert.Equal([0x10, 0x20], tx.Payload);
                Assert.Equal([0x03], tx.Suffix);

                var rx = store.ReadRxMessage(0);
                Assert.Equal(rxTime, rx.TimestampUtc);
                Assert.Equal(SessionCheckValidity.Invalid, rx.CheckValidity);
                Assert.Equal([0xAA, 0xBB], rx.Prefix);
                Assert.Equal([0x30], rx.Payload);
                Assert.Equal([0xCC, 0xDD], rx.Suffix);

                var diagnostic = Assert.IsType<DiagnosticSessionRecord>(store.ReadEvent(2));
                Assert.Equal(diagnosticTime, diagnostic.TimestampUtc);
                Assert.Equal("check failed: µ", diagnostic.Text);
            }

            Assert.Equal([0x53, 0x42, 0x53, 0x53, 0x01, 0x00], File.ReadAllBytes(path)[..6]);

            using var reopened = new SessionStore(path);
            Assert.Equal(3, reopened.EventCount);
            Assert.Equal([1], reopened.RxMessageEvents);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RecordLengthsAndIntegersAreLittleEndian()
    {
        var path = TempPath();
        try
        {
            var timestamp = new DateTime(0x0102030405060708, DateTimeKind.Utc);
            using (var store = new SessionStore(path))
            {
                store.AppendTxMessage(timestamp, [0xA1], [0xB1, 0xB2], [0xC1]);
                store.Flush();
            }

            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)SessionRecordType.TxMessage, bytes[6]);
            Assert.Equal([0x17, 0x00, 0x00, 0x00], bytes[7..11]); // 19 fixed + 4 data = 23
            Assert.Equal([0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01], bytes[11..19]);
            Assert.Equal(0x01, bytes[19]);
            Assert.Equal([0x02, 0x00, 0x00, 0x00], bytes[20..24]);
            Assert.Equal(0x01, bytes[24]);
            Assert.Equal([0xA1, 0xB1, 0xB2, 0xC1], bytes[25..29]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReopenDiscardsIncompleteFinalRecordAndContinuesAppending()
    {
        var path = TempPath();
        try
        {
            var timestamp = DateTime.UnixEpoch;
            using (var store = new SessionStore(path))
            {
                store.AppendDiagnostic(timestamp, "complete");
                store.Flush();
            }

            var validLength = new FileInfo(path).Length;
            using (var append = new FileStream(path, FileMode.Append, FileAccess.Write))
            using (var writer = new BinaryWriter(append, Encoding.UTF8))
            {
                writer.Write((byte)SessionRecordType.Diagnostic);
                writer.Write(100);
                writer.Write(timestamp.Ticks);
                writer.Write((byte)0x41);
            }

            using (var recovered = new SessionStore(path))
            {
                Assert.Equal(1, recovered.EventCount);
                Assert.Equal(validLength, new FileInfo(path).Length);
                recovered.AppendDiagnostic(timestamp.AddTicks(1), "after recovery");
            }

            using var reopened = new SessionStore(path);
            Assert.Equal(2, reopened.EventCount);
            Assert.Equal("after recovery", Assert.IsType<DiagnosticSessionRecord>(reopened.ReadEvent(1)).Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReopenStopsAtStructurallyInvalidRecord()
    {
        var path = TempPath();
        try
        {
            var timestamp = DateTime.UnixEpoch;
            using (var store = new SessionStore(path))
            {
                store.AppendDiagnostic(timestamp, "good");
                store.Flush();
            }

            var validLength = new FileInfo(path).Length;
            using (var append = new FileStream(path, FileMode.Append, FileAccess.Write))
            using (var writer = new BinaryWriter(append, Encoding.UTF8))
            {
                writer.Write((byte)SessionRecordType.TxMessage);
                writer.Write(19);
                writer.Write(timestamp.Ticks);
                writer.Write((byte)1); // prefix
                writer.Write(100);     // payload; impossible within a 19-byte record
                writer.Write((byte)0); // suffix
            }

            using var recovered = new SessionStore(path);
            Assert.Equal(1, recovered.EventCount);
            Assert.Equal(validLength, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsInvalidHeaderTimestampAndCheckValidity()
    {
        var path = TempPath();
        try
        {
            File.WriteAllBytes(path, [0x4E, 0x4F, 0x50, 0x45, 0x01, 0x00]);
            Assert.Throws<InvalidDataException>(() => new SessionStore(path));
            File.Delete(path);

            using var store = new SessionStore(path);
            Assert.Throws<ArgumentException>(() => store.AppendDiagnostic(DateTime.Now, "local"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                store.AppendRxMessage(DateTime.UtcNow, (SessionCheckValidity)99, [], [], []));
        }
        finally { File.Delete(path); }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"SerialBench-{Guid.NewGuid():N}.sbss");
}
