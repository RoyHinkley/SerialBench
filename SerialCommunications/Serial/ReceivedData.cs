using System.Text;

namespace SerialPortTerminal.Serial;

/// <summary>Immutable data retained for one receive unit recognized by <see cref="SerialDevice"/>.</summary>
/// <remarks>The received bytes and CRC diagnostics are evidence; presentation choices are deliberately kept outside this record.</remarks>
public sealed class ReceivedData
{
    private static readonly Encoding Latin1 = Encoding.Latin1;
    private readonly byte[] wireBytes;
    private readonly byte[] payloadBytes;

    public ReceivedData(byte[] wireBytes, byte[] payloadBytes, bool? crcValid,
        ushort? receivedCrc = null, ushort? calculatedResidue = null, ushort? expectedResidue = null)
    {
        ArgumentNullException.ThrowIfNull(wireBytes);
        ArgumentNullException.ThrowIfNull(payloadBytes);
        this.wireBytes = (byte[])wireBytes.Clone();
        this.payloadBytes = (byte[])payloadBytes.Clone();
        CrcValid = crcValid;
        ReceivedCrc = receivedCrc;
        CalculatedResidue = calculatedResidue;
        ExpectedResidue = expectedResidue;
    }

    public ReadOnlyMemory<byte> WireBytes => wireBytes;
    public ReadOnlyMemory<byte> PayloadBytes => payloadBytes;
    public bool? CrcValid { get; }

    /// <summary>The 16-bit CRC value carried by the message, interpreted using the configured byte order.</summary>
    public ushort? ReceivedCrc { get; }

    /// <summary>The CRC remainder after the complete received codeword was processed.</summary>
    public ushort? CalculatedResidue { get; }

    /// <summary>The residue required for a valid codeword by the active CRC configuration.</summary>
    public ushort? ExpectedResidue { get; }

    /// <summary>The CRC bytes exactly as received on the wire.</summary>
    public ReadOnlyMemory<byte> CrcBytes => CrcValid is not null && wireBytes.Length >= 2
        ? wireBytes.AsMemory(wireBytes.Length - 2, 2)
        : ReadOnlyMemory<byte>.Empty;

    public string PayloadText => Latin1.GetString(payloadBytes);
}
