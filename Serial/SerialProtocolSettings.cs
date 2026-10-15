using SerialPortTerminal.Serial.Protocol;

namespace SerialPortTerminal.Serial;

/// <summary>Immutable snapshot of settings that determine serial message framing and CRC behavior.</summary>
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
    bool SuppressCrcErrors)
{
    public static SerialProtocolSettings From(CrcOptions? crc, bool suppressCrcErrors) => crc is null
        ? new(false, 0, 0, 0, false, false, false, 0, true, suppressCrcErrors)
        : new(true, crc.InitialValue, crc.Polynomial, crc.ExpectedResidue, crc.PostInvert,
            crc.MsBitFirst, crc.MsByteFirst, crc.TermChar, crc.OmitTermChar, suppressCrcErrors);

    /// <summary>Creates a fresh mutable CRC options object for one transport operation.</summary>
    public CrcOptions? CreateCrcOptions() => !UseCrc ? null : new CrcOptions(
        InitialValue, Polynomial, ExpectedResidue, PostInvert, MsBitFirst, MsByteFirst, TermChar, OmitTermChar);

    /// <summary>Creates the receive decoder represented by this settings snapshot.</summary>
    internal IMessageDecoder CreateMessageDecoder()
    {
        IMessageIntegrity integrity = UseCrc
            ? new Crc16MessageIntegrity(CreateCrcOptions()!)
            : NoMessageIntegrity.Instance;

        IMessageFramer framer = UseCrc && !OmitTermChar
            ? new TrailerAwareTerminatorMessageFramer(TermChar, integrity)
            : new SilenceMessageFramer();

        return new MessageDecoder(framer, integrity);
    }
}
