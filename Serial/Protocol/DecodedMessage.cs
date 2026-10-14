namespace SerialPortTerminal.Serial.Protocol;

/// <summary>One framed wire message together with its integrity result.</summary>
/// <remarks>
/// <see cref="Frame"/> contains the bytes emitted by the framer after framing bytes have been removed.
/// <see cref="Payload"/> contains the application bytes only when integrity validation succeeds. A failed
/// integrity check remains a decoded message so diagnostics can preserve the exact received frame.
/// </remarks>
public sealed record DecodedMessage(ReadOnlyMemory<byte> Frame, ReadOnlyMemory<byte> Payload, bool IntegrityValid);
