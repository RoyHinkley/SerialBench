namespace SerialCommunications.Serial.Protocol;

[Obsolete("Use NoMessageCheck.")]
public sealed class NoMessageIntegrity : IMessageIntegrity
{
    public static NoMessageIntegrity Instance { get; } = new();
    private NoMessageIntegrity() { }
    public int FieldLength => NoMessageCheck.Instance.FieldLength;
    public byte[] Append(ReadOnlySpan<byte> payload) => NoMessageCheck.Instance.Append(payload);
    public bool TryValidate(ReadOnlySpan<byte> frame, out byte[] payload) => NoMessageCheck.Instance.TryValidate(frame, out payload);
}
