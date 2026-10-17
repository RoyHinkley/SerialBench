using SerialCommunications.Serial.Protocol;

namespace SerialCommunications.Serial;

/// <summary>Immutable snapshot of settings that determine serial message framing and checking behavior.</summary>
/// <remarks>
/// A snapshot is deliberately value-based: UI/configuration edits must never mutate protocol state that a
/// transmit or receive operation is already using. A new snapshot can therefore be staged and adopted only
/// at a known message boundary.
/// </remarks>
public sealed record SerialProtocolSettings(
    bool UseCrc,
    ushort InitialValue,
    ushort Polynomial,
    ushort ExpectedResidue,
    bool PostInvert,
    bool MsBitFirst,
    bool MsByteFirst,
    byte TermChar,
    bool OmitTermChar,
    bool SuppressInvalidMessages)
{
    [Obsolete("Use SuppressInvalidMessages.")]
    public bool SuppressCrcErrors => SuppressInvalidMessages;

    /// <summary>Creates a protocol snapshot from legacy CRC options and invalid-message delivery policy.</summary>
    public static SerialProtocolSettings From(CrcOptions? crc, bool suppressInvalidMessages) => crc is null
        ? new(false, 0, 0, 0, false, false, false, 0, true, suppressInvalidMessages)
        : new(true, crc.InitialValue, crc.Polynomial, crc.ExpectedResidue, crc.PostInvert,
            crc.MsBitFirst, crc.MsByteFirst, crc.TermChar, crc.OmitTermChar, suppressInvalidMessages);

    /// <summary>Creates a fresh mutable CRC options object for one transport operation.</summary>
    public CrcOptions? CreateCrcOptions() => !UseCrc ? null : new CrcOptions(
        InitialValue, Polynomial, ExpectedResidue, PostInvert, MsBitFirst, MsByteFirst, TermChar, OmitTermChar);

    /// <summary>Returns whether two snapshots use the same receive framing strategy.</summary>
    public bool UsesSameFramingStrategyAs(SerialProtocolSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return UseCrc == other.UseCrc && OmitTermChar == other.OmitTermChar;
    }

    /// <summary>Creates the receive decoder represented by this settings snapshot.</summary>
    internal IMessageDecoder CreateMessageDecoder()
    {
        IMessageCheck check = UseCrc
            ? new Crc16MessageCheck(CreateCrcOptions()!)
            : NoMessageCheck.Instance;

        IMessageFramer framer = UseCrc && !OmitTermChar
            ? new TrailerAwareTerminatorMessageFramer(TermChar, check)
            : new SilenceMessageFramer();

        return new MessageDecoder(framer, check);
    }
}
