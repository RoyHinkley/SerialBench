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
            Evaluate(frameReady);
        }
    }

    public void SilenceElapsed(Action<ReadOnlyMemory<byte>> frameReady)
    {
        ArgumentNullException.ThrowIfNull(frameReady);
        if (pendingTerminator >= 0)
            EmitAtPendingTerminator(frameReady);
    }

    public void Reset()
    {
        buffer.Clear();
        pendingTerminator = -1;
    }

    private void Evaluate(Action<ReadOnlyMemory<byte>> frameReady)
    {
        while (true)
        {
            var changed = false;
            for (var index = 0; index < buffer.Count; index++)
            {
                if (buffer[index] != terminator)
                    continue;

                if (TryEmitCandidate(index, frameReady))
                {
                    changed = true;
                    break;
                }

                if (pendingTerminator < 0)
                    pendingTerminator = index;

                if (index - pendingTerminator > integrity.FieldLength)
                {
                    EmitAtPendingTerminator(frameReady);
                    changed = true;
                    break;
                }
            }

            if (!changed)
                return;
        }
    }

    private bool TryEmitCandidate(int terminatorIndex, Action<ReadOnlyMemory<byte>> frameReady)
    {
        if (terminatorIndex < integrity.FieldLength)
            return false;

        var candidate = buffer.GetRange(0, terminatorIndex).ToArray();
        if (!integrity.TryValidate(candidate, out var payload) || payload.Contains(terminator))
            return false;

        frameReady(candidate);
        buffer.RemoveRange(0, terminatorIndex + 1);
        pendingTerminator = -1;
        return true;
    }

    private void EmitAtPendingTerminator(Action<ReadOnlyMemory<byte>> frameReady)
    {
        var frame = buffer.GetRange(0, pendingTerminator).ToArray();
        frameReady(frame);
        buffer.RemoveRange(0, pendingTerminator + 1);
        pendingTerminator = -1;
    }
}
