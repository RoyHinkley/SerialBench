namespace SerialPortTerminal.Serial.Protocol;

/// <summary>
/// Frames protocols whose fixed-width integrity field immediately precedes a reserved terminator.
/// </summary>
/// <remarks>
/// The terminator is forbidden in payload bytes but may occur in the integrity field. A terminator therefore
/// cannot be classified until enough subsequent bytes arrive to distinguish a real boundary from an integrity
/// byte. Invalid candidates are retained until a later terminator provides an unambiguous resynchronization
/// point. This is the framing pattern used by the Aeon payload + CRC + ETX protocol.
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

    /// <summary>Number of invalid prefixes discarded while resynchronizing the byte stream.</summary>
    public uint RejectedPrefixes { get; private set; }

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
        RejectedPrefixes = 0;
    }

    private void TryCompleteCandidate(Action<ReadOnlyMemory<byte>> frameReady)
    {
        while (buffer.Count > integrity.FieldLength && buffer[^1] == terminator)
        {
            var candidate = buffer.GetRange(0, buffer.Count - 1).ToArray();
            if (integrity.TryValidate(candidate, out var payload) && !payload.Contains(terminator))
            {
                frameReady(candidate);
                buffer.Clear();
                return;
            }

            // With a fixed-width integrity field, a terminator before that field cannot belong to a valid
            // candidate ending at the current byte. Discard through the first such terminator and retry the
            // same current boundary against the remaining bytes. If there is no such byte yet, the current
            // terminator may itself be an integrity byte, so more input is required before deciding.
            var payloadLength = candidate.Length - integrity.FieldLength;
            var invalidTerminator = candidate.AsSpan(0, payloadLength).IndexOf(terminator);
            if (invalidTerminator < 0)
                return;

            buffer.RemoveRange(0, invalidTerminator + 1);
            RejectedPrefixes++;
        }
    }
}
