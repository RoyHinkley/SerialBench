using System.Text;

namespace SerialCommunications.Serial;

/// <summary>Immutable data retained for one receive unit recognized by <see cref="SerialChannel"/>.</summary>
/// <remarks>The received bytes and check diagnostics are evidence; presentation choices are deliberately kept outside this record.</remarks>
public sealed class ReceivedData
{
    private static readonly Encoding Latin1 = Encoding.Latin1;
    private readonly byte[] wireBytes;
    private readonly byte[] payloadBytes;
    private readonly bool crcCheck;

    public ReceivedData(byte[] wireBytes, byte[] payloadBytes, bool? crcValid,
        ushort? receivedCrc = null, ushort? calculatedResidue = null, ushort? expectedResidue = null)
        : this(wireBytes, payloadBytes, crcValid, crcValid is not null, receivedCrc, calculatedResidue, expectedResidue)
    {
    }

    internal ReceivedData(byte[] wireBytes, byte[] payloadBytes, bool? checkValid, bool crcCheck,
        ushort? receivedCrc = null, ushort? calculatedResidue = null, ushort? expectedResidue = null)
    {
        ArgumentNullException.ThrowIfNull(wireBytes);
        ArgumentNullException.ThrowIfNull(payloadBytes);
        this.wireBytes = (byte[])wireBytes.Clone();
        this.payloadBytes = (byte[])payloadBytes.Clone();
        this.crcCheck = crcCheck;
        CheckValid = checkValid;
        ReceivedCrc = receivedCrc;
        CalculatedResidue = calculatedResidue;
        ExpectedResidue = expectedResidue;
    }

    public ReadOnlyMemory<byte> WireBytes => wireBytes;
    public ReadOnlyMemory<byte> PayloadBytes => payloadBytes;

    /// <summary>Whether the configured message check validated; null when no check is configured.</summary>
    public bool? CheckValid { get; }

    /// <summary>CRC-specific alias for <see cref="CheckValid"/>; null when the configured check is not CRC-16.</summary>
    public bool? CrcValid => crcCheck ? CheckValid : null;

    /// <summary>The 16-bit CRC value carried by the message, interpreted using the configured byte order.</summary>
    public ushort? ReceivedCrc { get; }

    /// <summary>The CRC remainder after the complete received codeword was processed.</summary>
    public ushort? CalculatedResidue { get; }

    /// <summary>The residue required for a valid codeword by the active CRC configuration.</summary>
    public ushort? ExpectedResidue { get; }

    /// <summary>The CRC bytes exactly as received on the wire.</summary>
    public ReadOnlyMemory<byte> CrcBytes => crcCheck && wireBytes.Length >= 2
        ? wireBytes.AsMemory(wireBytes.Length - 2, 2)
        : ReadOnlyMemory<byte>.Empty;

    public string PayloadText => Latin1.GetString(payloadBytes);
}
