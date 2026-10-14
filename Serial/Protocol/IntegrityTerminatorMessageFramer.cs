namespace SerialPortTerminal.Serial.Protocol;

/// <summary>
/// Frames protocols whose fixed-width integrity field immediately precedes a reserved terminator.
/// </summary>
/// <remarks>
/// The terminator is forbidden in payload bytes but may occur in the integrity field. A terminator therefore
/// cannot be classified until enough subsequent bytes arrive to distinguish a real boundary from an integrity
/// byte. Integrity is used only to resolve that defined ambiguity; this framer does not search corrupt data for
/// an embedded CRC-valid suffix. Once a candidate is demonstrably malformed, the entire ambiguous sequence is
/// discarded through the current terminator. A framing fault may therefore also sacrifice a following intact
/// message, after which normal framing naturally restores synchronization.
/// </remarks>
public sealed class IntegrityTerminatorMessageFramer : IMessageFramer
{
    private readonly byte terminator;
    private readonly IMessageIntegrity integrity;
    private readonly List<byte> buffer = [];

    public IntegrityTerminatorMessageFramer(byte terminator, IMessageIntegrity integrity)
    {
        ArgumentNullException.ThrowIfNull(integrity);
        if (integrity.FieldLength <= 0)
            throw new ArgumentException("Integrity-aware terminator framing requires a non-empty integrity field.", nameof(integrity));

        this.terminator = terminator;
        this.integrity = integrity;
    }

    /// <summary>Number of malformed ambiguous sequences discarded by the framer.</summary>
    public uint RejectedSequences { get; private set; }

    public void Consume(ReadOnlySpan<byte> bytes, Action<ReadOnlyMemory<byte>> frameReady)
    {
        ArgumentNullException.ThrowIfNull(frameReady);

        foreach (var value in bytes)
        {
            buffer.Add(value);
            if (value == terminator)
                TryCompleteCandidate(frameReady);
        }
    }

    public void SilenceElapsed(Action<ReadOnlyMemory<byte>> frameReady)
    {
        ArgumentNullException.ThrowIfNull(frameReady);
    }

    public void Reset()
    {
        buffer.Clear();
        RejectedSequences = 0;
    }

    private void TryCompleteCandidate(Action<ReadOnlyMemory<byte>> frameReady)
    {
        if (buffer.Count <= integrity.FieldLength)
            return;

        var candidate = buffer.GetRange(0, buffer.Count - 1).ToArray();
        if (integrity.TryValidate(candidate, out var payload) && !payload.Contains(terminator))
        {
            frameReady(candidate);
            buffer.Clear();
            return;
        }

        // If no earlier terminator lies in the candidate payload region, the current terminator may itself be
        // one of the fixed-width integrity bytes. More input is required before it can be classified safely.
        var payloadLength = candidate.Length - integrity.FieldLength;
        if (payloadLength <= 0 || candidate.AsSpan(0, payloadLength).IndexOf(terminator) < 0)
            return;

        // An earlier terminator has now aged far enough into the candidate to prove that the accumulated sequence
        // cannot be one valid frame. Do not search within corrupt data for a convenient CRC-valid suffix: discard
        // the whole ambiguous sequence through this boundary and let a subsequent normally framed message resync.
        buffer.Clear();
        RejectedSequences++;
    }
}
