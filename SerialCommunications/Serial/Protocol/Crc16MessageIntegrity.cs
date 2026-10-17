namespace SerialCommunications.Serial.Protocol;

[Obsolete("Use Crc16MessageCheck.")]
public sealed class Crc16MessageIntegrity : IMessageIntegrity
{
    private readonly Crc16MessageCheck check;
    public Crc16MessageIntegrity(CrcOptions options) => check = new Crc16MessageCheck(options);
    public int FieldLength => check.FieldLength;
    public byte[] Append(ReadOnlySpan<byte> payload) => check.Append(payload);
    public bool TryValidate(ReadOnlySpan<byte> frame, out byte[] payload) => check.TryValidate(frame, out payload);
}
