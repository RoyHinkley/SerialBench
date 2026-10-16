using SerialPortTerminal.Serial;
using SerialPortTerminal.Serial.Protocol;

namespace SerialPortTerminal.Tests;

public sealed class MessageIntegrityTests
{
    private static CrcOptions AeonLikeCrc() => new()
    {
        Polynomial = 0xA001,
        InitialValue = 0xFFFF,
        ExpectedResidue = 0x0000,
        PostInvert = false,
        MsBitFirst = false,
        MsByteFirst = false,
        TermChar = 0x03,
        OmitTermChar = false
    };

    [Fact]
    public void No_integrity_preserves_exact_payload()
    {
        var integrity = NoMessageIntegrity.Instance;
        var payload = new byte[] { 0x00, 0x03, 0xFF };

        var frame = integrity.Append(payload);

        Assert.Equal(payload, frame);
        Assert.True(integrity.TryValidate(frame, out var decoded));
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Crc_integrity_appends_only_crc_not_framing()
    {
        var options = AeonLikeCrc();
        var integrity = new Crc16MessageIntegrity(options);
        var payload = new byte[] { 0x41, 0x42, 0x43 };

        var frame = integrity.Append(payload);
        var legacyWire = new Crc(options).Append(payload);

        Assert.Equal(payload.Length + 2, frame.Length);
        Assert.Equal(legacyWire[..^1], frame);
        Assert.True(integrity.TryValidate(frame, out var decoded));
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Crc_integrity_rejects_corruption_without_discarding_candidate_payload()
    {
        var integrity = new Crc16MessageIntegrity(AeonLikeCrc());
        var frame = integrity.Append(new byte[] { 0x10, 0x20, 0x30 });
        frame[1] ^= 0x01;

        Assert.False(integrity.TryValidate(frame, out var decoded));
        Assert.Equal(new byte[] { 0x10, 0x21, 0x30 }, decoded);
    }

    [Fact]
    public void Crc_integrity_accepts_crc_bytes_equal_to_protocol_terminator()
    {
        var options = AeonLikeCrc();
        var integrity = new Crc16MessageIntegrity(options);
        var payload = FindPayload(options, frame => frame[^2] == options.TermChar || frame[^1] == options.TermChar);
        var frame = integrity.Append(payload);

        Assert.Contains(options.TermChar, frame[^2..]);
        Assert.True(integrity.TryValidate(frame, out var decoded));
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Crc_integrity_snapshots_mutable_legacy_options()
    {
        var options = AeonLikeCrc();
        var integrity = new Crc16MessageIntegrity(options);
        var payload = new byte[] { 0x51, 0x52, 0x53 };
        var frame = integrity.Append(payload);

        options.Polynomial = 0x1021;
        options.InitialValue = 0;

        Assert.True(integrity.TryValidate(frame, out var decoded));
        Assert.Equal(payload, decoded);
    }

    private static byte[] FindPayload(CrcOptions options, Func<byte[], bool> predicate)
    {
        for (var value = 0; value <= ushort.MaxValue; value++)
        {
            var payload = new byte[] { (byte)value, (byte)(value >> 8) };
            if (payload.Contains(options.TermChar)) continue;
            var frame = new Crc16MessageIntegrity(options).Append(payload);
            if (predicate(frame)) return payload;
        }

        throw new InvalidOperationException("Unable to synthesize requested CRC edge case.");
    }
}
