namespace SerialCommunications.Serial.Protocol;

/// <summary>Integrity strategy for protocols that carry payload bytes without an integrity field.</summary>
public sealed class NoMessageIntegrity : IMessageIntegrity
{
    public static NoMessageIntegrity Instance { get; } = new();

    private NoMessageIntegrity() { }

    public int FieldLength => 0;

    public byte[] Append(ReadOnlySpan<byte> payload) => payload.ToArray();

    public bool TryValidate(ReadOnlySpan<byte> frame, out byte[] payload)
    {
        payload = frame.ToArray();
        return true;
    }
}
