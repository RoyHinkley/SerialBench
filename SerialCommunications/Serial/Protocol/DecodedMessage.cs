namespace SerialCommunications.Serial.Protocol;

/// <summary>One framed wire message together with its check result.</summary>
/// <remarks>
/// <see cref="Frame"/> contains the bytes emitted by the framer after framing bytes have been removed.
/// <see cref="Payload"/> contains the application bytes with the check field removed. A failed check remains
/// a decoded message so diagnostics can preserve the exact received frame and candidate payload.
/// </remarks>
/// <param name="Frame">The framed wire bytes, excluding framing bytes removed by the framer.</param>
/// <param name="Payload">The candidate application payload after removal of the check field.</param>
/// <param name="CheckValid">Whether the configured message check accepted the frame.</param>
public sealed record DecodedMessage(ReadOnlyMemory<byte> Frame, ReadOnlyMemory<byte> Payload, bool CheckValid);
