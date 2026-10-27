using SerialBench.Application;

namespace SerialBench.Tests;

public sealed class SessionTextRendererTests
{
    [Fact]
    public void ReceivedRenderingUsesPayloadOnly()
    {
        var record = new RxSessionRecord(DateTime.UnixEpoch, SessionCheckValidity.Invalid,
            [0x02], [0x41, 0x0D, 0x03], [0x7E]);

        Assert.Equal(@"A\r\x03", SessionTextRenderer.RenderReceived(record, ReceivedDataFormat.Text));
        Assert.Equal("41 0D 03", SessionTextRenderer.RenderReceived(record, ReceivedDataFormat.Bytes));
    }

    [Fact]
    public void TraceRenderingPreservesPhysicalMessageSegments()
    {
        var timestamp = new DateTime(638900000000000000, DateTimeKind.Utc);
        var tx = new TxSessionRecord(timestamp, [0x02], [0x41], [0x12, 0x34, 0x03]);
        var rx = new RxSessionRecord(timestamp, SessionCheckValidity.Invalid, [0x99, 0x02], [0x42], [0x56, 0x78, 0x03]);

        var txText = SessionTextRenderer.RenderTrace(tx);
        var rxText = SessionTextRenderer.RenderTrace(rx);

        Assert.Contains("TX  PRE[02] P[41] SUF[12 34 03]", txText);
        Assert.Contains("RX  PRE[99 02] P[42] SUF[56 78 03]  CHECK INVALID", rxText);
    }

    [Fact]
    public void TraceDiagnosticTextIsPreserved()
    {
        var record = new DiagnosticSessionRecord(DateTime.UnixEpoch, "Acquired: 01 02 03");
        Assert.EndsWith("Acquired: 01 02 03", SessionTextRenderer.RenderTrace(record));
    }

    [Fact]
    public void WindowRenderingSeparatesRecordsWithoutAddingTrailingNewline()
    {
        var records = new[]
        {
            new RxSessionRecord(DateTime.UnixEpoch, SessionCheckValidity.Ignore, [], [0x41], []),
            new RxSessionRecord(DateTime.UnixEpoch, SessionCheckValidity.Ignore, [], [0x42], []),
        };

        Assert.Equal($"A{Environment.NewLine}B", SessionTextRenderer.RenderReceivedWindow(records, ReceivedDataFormat.Text));
    }
}
