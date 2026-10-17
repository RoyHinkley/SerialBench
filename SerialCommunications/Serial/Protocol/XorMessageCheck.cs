namespace SerialCommunications.Serial.Protocol;

/// <summary>Appends and validates a one-byte XOR check field.</summary>
/// <remarks>
/// The check byte is the exclusive OR of <see cref="InitialValue"/> and every payload byte. The default
/// initial value is zero, matching the BCC calculation used by surveyed Eurotherm protocols. The protocol
/// may call the resulting field a BCC; XOR is the calculation performed by this type.
/// </remarks>
public sealed class XorMessageCheck : IMessageCheck
{
    /// <summary>Creates an XOR check using the specified initial accumulator value.</summary>
    /// <param name="initialValue">Value XORed with the payload bytes before the check byte is produced.</param>
    public XorMessageCheck(byte initialValue = 0) => InitialValue = initialValue;

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
        var value = InitialValue;
        foreach (var b in bytes) value ^= b;
        return value;
    }
}
