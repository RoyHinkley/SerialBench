namespace SerialPortTerminal.Serial.Protocol;

/// <summary>
/// Frames a stream terminated by a reserved byte when that byte may also occur in a fixed-width trailer.
/// </summary>
/// <remarks>
/// A terminator normally establishes a message boundary whether or not the resulting frame passes integrity
/// validation. Integrity is consulted only to resolve the protocol-defined ambiguity where a terminator-valued
/// byte may instead belong to the trailer. A bad frame is therefore still a frame; validation belongs downstream.
/// </remarks>
public sealed class TrailerAwareTerminatorMessageFramer : IMessageFramer
{
    private readonly byte terminator;
    private readonly IMessageIntegrity integrity;
    private readonly List<byte> buffer = [];
    private int pendingTerminator = -1;

    public TrailerAwareTerminatorMessageFramer(byte terminator, IMessageIntegrity integrity)
    {
        ArgumentNullException.ThrowIfNull(integrity);
        if (integrity.FieldLength <= 0)
            throw new ArgumentException("Trailer-aware framing requires a non-empty fixed-width trailer.", nameof(integrity));

        this.terminator = terminator;
        this.integrity = integrity;
    }

    public void Consume(ReadOnlySpan<byte> bytes, Action<ReadOnlyMemory<byte>> frameReady)
    {
        ArgumentNullException.ThrowIfNull(frameReady);

        foreach (var value in bytes)
        {
            buffer.Add(value);

            if (pendingTerminator >= 0)
            {
                if (value == terminator && TryEmitCurrentCandidate(frameReady))
                    continue;

                if (buffer.Count - pendingTerminator - 1 > integrity.FieldLength)
                    EmitAtPendingTerminator(frameReady);
            }

            if (value != terminator)
                continue;

            if (TryEmitCurrentCandidate(frameReady))
                continue;

            pendingTerminator = buffer.Count - 1;
        }
    }

    public void SilenceElapsed(Action<ReadOnlyMemory<byte>> frameReady)
    {
        ArgumentNullException.ThrowIfNull(frameReady);
    }

    public void Reset()
    {
        buffer.Clear();
        pendingTerminator = -1;
    }

    private bool TryEmitCurrentCandidate(Action<ReadOnlyMemory<byte>> frameReady)
    {
        if (buffer.Count <= integrity.FieldLength)
            return false;

        var candidate = buffer.GetRange(0, buffer.Count - 1).ToArray();
        if (!integrity.TryValidate(candidate, out var payload) || payload.Contains(terminator))
            return false;

        frameReady(candidate);
        buffer.Clear();
        pendingTerminator = -1;
        return true;
    }

    private void EmitAtPendingTerminator(Action<ReadOnlyMemory<byte>> frameReady)
    {
        var frame = buffer.GetRange(0, pendingTerminator).ToArray();
        var remainder = buffer.GetRange(pendingTerminator + 1, buffer.Count - pendingTerminator - 1);

        frameReady(frame);
        buffer.Clear();
        buffer.AddRange(remainder);
        pendingTerminator = buffer.LastIndexOf(terminator);
    }
}
