namespace SerialCommunications.Serial.Protocol;

/// <summary>Appends and validates a one-byte additive checksum.</summary>
/// <remarks>
/// The checksum is the low eight bits of <see cref="InitialValue"/> plus every payload byte. The default
/// initial value is zero, matching the additive checksum calculation found in surveyed AeonHacs protocols.
/// This type operates on the binary check byte only; protocol-specific representations such as two ASCII
/// hexadecimal characters belong in the protocol adapter rather than in the checksum calculation.
/// </remarks>
public sealed class ChecksumMessageCheck : IMessageCheck
{
    /// <summary>Creates an additive checksum using the specified initial accumulator value.</summary>
    /// <param name="initialValue">Value added before the payload bytes. Only the low eight bits are retained.</param>
    public ChecksumMessageCheck(byte initialValue = 0) => InitialValue = initialValue;

    /// <summary>Initial accumulator value. The default is zero.</summary>
    public byte InitialValue { get; }

    /// <inheritdoc />
    public int FieldLength => 1;

    /// <inheritdoc />
    public byte[] Append(ReadOnlySpan<byte> payload)
    {
        var result = new byte[payload.Length + 1];
        payload.CopyTo(result);
        result[^1] = Calculate(payload);
        return result;
    }

    /// <inheritdoc />
    public bool TryValidate(ReadOnlySpan<byte> frame, out byte[] payload)
    {
        if (frame.Length < FieldLength)
        {
            payload = [];
            return false;
        }

        payload = frame[..^1].ToArray();
        return frame[^1] == Calculate(payload);
    }

    private byte Calculate(ReadOnlySpan<byte> bytes)
    {
        var sum = (int)InitialValue;
        foreach (var b in bytes) sum += b;
        return (byte)sum;
    }
}
