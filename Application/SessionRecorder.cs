namespace SerialBench.Application;

/// <summary>Records live SerialBench activity into the authoritative append-only session store.</summary>
/// <remarks>
/// Framing decomposition is supplied explicitly by the caller. The recorder never guesses payload
/// position by searching wire bytes because framing bytes may legitimately contain payload values.
/// </remarks>
public sealed class SessionRecorder
{
    private readonly SessionStore store;

    public SessionRecorder(SessionStore store) => this.store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Raised after a complete record has been appended to the store.</summary>
    public event Action<int, SessionRecord>? EventAppended;

    public int RecordTx(DateTime timestampUtc, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> suffix)
    {
        var record = new TxSessionRecord(timestampUtc, prefix.ToArray(), payload.ToArray(), suffix.ToArray());
        var ordinal = store.AppendTxMessage(timestampUtc, prefix, payload, suffix);
        EventAppended?.Invoke(ordinal, record);
        return ordinal;
    }

    public int RecordRx(DateTime timestampUtc, SessionCheckValidity checkValidity,
        ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> suffix)
    {
        var record = new RxSessionRecord(timestampUtc, checkValidity, prefix.ToArray(), payload.ToArray(), suffix.ToArray());
        var ordinal = store.AppendRxMessage(timestampUtc, checkValidity, prefix, payload, suffix);
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
}
