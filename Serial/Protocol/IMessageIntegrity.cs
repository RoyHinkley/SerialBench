namespace SerialPortTerminal.Serial.Protocol;

/// <summary>
/// Adds and validates the integrity field associated with one protocol frame.
/// </summary>
/// <remarks>
/// Framing and integrity are deliberately separate concepts. An integrity strategy receives a frame whose
/// framing bytes have already been removed, and returns the payload after removing its integrity field.
/// Protocols in which integrity bytes participate in boundary recognition may provide a specialized framer
/// that collaborates with the same strategy.
/// </remarks>
public interface IMessageIntegrity
{
    /// <summary>Number of integrity bytes appended to a payload.</summary>
    int FieldLength { get; }

    /// <summary>Appends this strategy's integrity field to exact payload bytes.</summary>
    byte[] Append(ReadOnlySpan<byte> payload);

    /// <summary>Validates a payload plus integrity field and returns the payload when valid.</summary>
    bool TryValidate(ReadOnlySpan<byte> frame, out byte[] payload);
}
