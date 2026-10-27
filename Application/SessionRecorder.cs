using SerialCommunications.Serial;

namespace SerialBench.Application;

/// <summary>Records live SerialBench activity into the authoritative append-only session store.</summary>
public sealed class SessionRecorder
{
    private readonly SessionStore store;

    public SessionRecorder(SessionStore store) => this.store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Raised after a complete record has been appended to the store.</summary>
    public event Action<int, SessionRecord>? EventAppended;

    public int RecordTx(DateTime timestampUtc, ReadOnlySpan<byte> wireBytes, ReadOnlySpan<byte> payload)
    {
        var record = DecomposeTx(timestampUtc, wireBytes, payload);
        var ordinal = store.AppendTxMessage(timestampUtc, record.Prefix, record.Payload, record.Suffix);
        EventAppended?.Invoke(ordinal, record);
        return ordinal;
    }

    public int RecordRx(DateTime timestampUtc, ReceivedData data)
    {
        var record = DecomposeRx(timestampUtc, data);
        var ordinal = store.AppendRxMessage(timestampUtc, record.CheckValidity, record.Prefix, record.Payload, record.Suffix);
        EventAppended?.Invoke(ordinal, record);
        return ordinal;
    }

    public int RecordDiagnostic(DateTime timestampUtc, string text)
    {
        var record = new DiagnosticSessionRecord(timestampUtc, text);
        var ordinal = store.AppendDiagnostic(timestampUtc, text);
        EventAppended?.Invoke(ordinal, record);
        return ordinal;
    }

    private static TxSessionRecord DecomposeTx(DateTime timestampUtc, ReadOnlySpan<byte> wireBytes, ReadOnlySpan<byte> payload)
    {
        var payloadOffset = wireBytes.IndexOf(payload);
        if (payloadOffset < 0) return new TxSessionRecord(timestampUtc, [], payload.ToArray(), []);
        return new TxSessionRecord(timestampUtc, wireBytes[..payloadOffset].ToArray(), payload.ToArray(), wireBytes[(payloadOffset + payload.Length)..].ToArray());
    }

    private static RxSessionRecord DecomposeRx(DateTime timestampUtc, ReceivedData data)
    {
        var wire = data.WireBytes.Span;
        var payload = data.PayloadBytes.Span;
        var payloadOffset = wire.IndexOf(payload);
        var prefix = payloadOffset < 0 ? Array.Empty<byte>() : wire[..payloadOffset].ToArray();
        var suffix = payloadOffset < 0 ? Array.Empty<byte>() : wire[(payloadOffset + payload.Length)..].ToArray();
        var validity = data.CheckValid switch
        {
            true => SessionCheckValidity.Valid,
            false => SessionCheckValidity.Invalid,
            null => SessionCheckValidity.Ignore,
        };
        return new RxSessionRecord(timestampUtc, validity, prefix, payload.ToArray(), suffix);
    }
}
