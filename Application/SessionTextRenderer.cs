using System.Text;

namespace SerialBench.Application;

/// <summary>Renders bounded session-record projections without changing the authoritative stored bytes.</summary>
public static class SessionTextRenderer
{
    /// <summary>Renders received payload bytes in the selected Communications representation.</summary>
    public static string RenderReceived(RxSessionRecord record, ReceivedDataFormat format) =>
        format == ReceivedDataFormat.Bytes
            ? FormatHex(record.Payload)
            : ByteExpression.Format(record.Payload);

    /// <summary>Renders one chronological session event for the Trace view.</summary>
    public static string RenderTrace(SessionRecord record)
    {
        var timestamp = record.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        return record switch
        {
            TxSessionRecord tx => $"{timestamp}  TX  {FormatMessage(tx.Prefix, tx.Payload, tx.Suffix)}",
            RxSessionRecord rx => $"{timestamp}  RX  {FormatMessage(rx.Prefix, rx.Payload, rx.Suffix)}{FormatCheckValidity(rx.CheckValidity)}",
            DiagnosticSessionRecord diagnostic => $"{timestamp}  {diagnostic.Text}",
            _ => throw new ArgumentOutOfRangeException(nameof(record)),
        };
    }

    /// <summary>Renders a bounded Received window as one TextBox body.</summary>
    public static string RenderReceivedWindow(IEnumerable<RxSessionRecord> records, ReceivedDataFormat format) =>
        string.Join(Environment.NewLine, records.Select(record => RenderReceived(record, format)));

    /// <summary>Renders a bounded Trace window as one TextBox body.</summary>
    public static string RenderTraceWindow(IEnumerable<SessionRecord> records) =>
        string.Join(Environment.NewLine, records.Select(RenderTrace));

    private static string FormatMessage(byte[] prefix, byte[] payload, byte[] suffix)
    {
        var builder = new StringBuilder();
        AppendSegment(builder, "PRE", prefix);
        AppendSegment(builder, "P", payload);
        AppendSegment(builder, "SUF", suffix);
        return builder.ToString();
    }

    private static void AppendSegment(StringBuilder builder, string label, byte[] bytes)
    {
        if (builder.Length > 0) builder.Append(' ');
        builder.Append(label).Append('[').Append(FormatHex(bytes)).Append(']');
    }

    private static string FormatCheckValidity(SessionCheckValidity validity) => validity switch
    {
        SessionCheckValidity.Invalid => "  CHECK INVALID",
        SessionCheckValidity.Valid => "  CHECK VALID",
        SessionCheckValidity.Ignore => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(validity)),
    };

    private static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;
        var builder = new StringBuilder(bytes.Length * 3 - 1);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(bytes[i].ToString("X2"));
        }
        return builder.ToString();
    }
}
