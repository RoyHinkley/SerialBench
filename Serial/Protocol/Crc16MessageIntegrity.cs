namespace SerialPortTerminal.Serial.Protocol;

/// <summary>16-bit CRC integrity strategy independent of message framing.</summary>
public sealed class Crc16MessageIntegrity : IMessageIntegrity
{
    private readonly CrcOptions options;

    public Crc16MessageIntegrity(CrcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = new CrcOptions(
            options.InitialValue,
            options.Polynomial,
            options.ExpectedResidue,
            options.PostInvert,
            options.MsBitFirst,
            options.MsByteFirst,
            options.TermChar,
            omitTermChar: true);
    }

    public int FieldLength => 2;

    public byte[] Append(ReadOnlySpan<byte> payload) => new Crc(options).Append(payload);

    public bool TryValidate(ReadOnlySpan<byte> frame, out byte[] payload)
    {
        if (frame.Length < FieldLength)
        {
            payload = [];
            return false;
        }

        var crc = new Crc(options);
        crc.Update(frame);
        if (!crc.Good())
        {
            payload = [];
            return false;
        }

        payload = frame[..^FieldLength].ToArray();
        return true;
    }
}
