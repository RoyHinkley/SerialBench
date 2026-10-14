using SerialPortTerminal.Serial;
using SerialPortTerminal.Serial.Protocol;

namespace SerialPortTerminal.Tests;

public sealed class IntegrityTerminatorMessageFramerTests
{
    private static CrcOptions AeonCrc() => new()
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
    public void Aeon_frame_is_independent_of_transport_chunking()
    {
        var options = AeonCrc();
        var payload = FindPayload(options, frame => frame[^2] == options.TermChar);
        var wire = new Crc(options).Append(payload);

        for (var split = 0; split <= wire.Length; split++)
        {
            var framer = CreateFramer(options);
            var frames = new List<byte[]>();
            framer.Consume(wire.AsSpan(0, split), frame => frames.Add(frame.ToArray()));
            framer.Consume(wire.AsSpan(split), frame => frames.Add(frame.ToArray()));

            var frame = Assert.Single(frames);
            Assert.Equal(wire[..^1], frame);
            Assert.True(new Crc16MessageIntegrity(options).TryValidate(frame, out var decoded));
            Assert.Equal(payload, decoded);
        }
    }

    [Fact]
    public void Both_crc_positions_may_equal_terminator()
    {
        var options = AeonCrc();
        foreach (var crcIndexFromEnd in new[] { 3, 2 })
        {
            var payload = FindPayload(options, wire => wire[^crcIndexFromEnd] == options.TermChar);
            var wire = new Crc(options).Append(payload);
            var framer = CreateFramer(options);
            var frames = new List<byte[]>();

            foreach (var value in wire)
                framer.Consume(new[] { value }, frame => frames.Add(frame.ToArray()));

            Assert.Single(frames);
            Assert.Equal(wire[..^1], frames[0]);
        }
    }

    [Fact]
    public void Reserved_terminator_in_payload_discards_only_proven_malformed_sequence()
    {
        var options = AeonCrc();
        var illegalPayload = new byte[] { 0x10, options.TermChar, 0x20, 0x30 };
        var invalidWire = new Crc(options).Append(illegalPayload);
        var validPayload = new byte[] { 0x41, 0x42, 0x43 };
        var validWire = new Crc(options).Append(validPayload);
        var framer = CreateFramer(options);
        var frames = new List<byte[]>();

        framer.Consume(invalidWire.Concat(validWire).ToArray(), frame => frames.Add(frame.ToArray()));

        var frame = Assert.Single(frames);
        Assert.Equal(validWire[..^1], frame);
        Assert.Equal(1u, framer.RejectedSequences);
    }

    [Fact]
    public void Corrupt_frame_may_sacrifice_following_valid_frame_rather_than_search_for_suffix()
    {
        var options = AeonCrc();
        var corrupt = new Crc(options).Append(new byte[] { 0x10, 0x20, 0x30 });
        corrupt[1] ^= 0x01;
        var sacrificed = new Crc(options).Append(new byte[] { 0x51, 0x52, 0x53 });
        var recovered = new Crc(options).Append(new byte[] { 0x61, 0x62, 0x63 });
        var framer = CreateFramer(options);
        var frames = new List<byte[]>();

        framer.Consume(corrupt.Concat(sacrificed).Concat(recovered).ToArray(), frame => frames.Add(frame.ToArray()));

        var frame = Assert.Single(frames);
        Assert.Equal(recovered[..^1], frame);
        Assert.Equal(1u, framer.RejectedSequences);
    }

    [Fact]
    public void Missing_final_terminator_may_sacrifice_following_valid_frame()
    {
        var options = AeonCrc();
        var unterminated = new Crc(options).Append(new byte[] { 0x10, 0x20, 0x30 })[..^1];
        var sacrificed = new Crc(options).Append(new byte[] { 0x41, 0x42, 0x43 });
        var recovered = new Crc(options).Append(new byte[] { 0x51, 0x52, 0x53 });
        var framer = CreateFramer(options);
        var frames = new List<byte[]>();

        framer.Consume(unterminated.Concat(sacrificed).Concat(recovered).ToArray(), frame => frames.Add(frame.ToArray()));

        var frame = Assert.Single(frames);
        Assert.Equal(recovered[..^1], frame);
        Assert.Equal(1u, framer.RejectedSequences);
    }

    [Fact]
    public void Back_to_back_frames_are_delivered_from_one_chunk()
    {
        var options = AeonCrc();
        var first = new Crc(options).Append(new byte[] { 0x10, 0x20 });
        var second = new Crc(options).Append(new byte[] { 0x30, 0x40 });
        var framer = CreateFramer(options);
        var frames = new List<byte[]>();

        framer.Consume(first.Concat(second).ToArray(), frame => frames.Add(frame.ToArray()));

        Assert.Equal(2, frames.Count);
        Assert.Equal(first[..^1], frames[0]);
        Assert.Equal(second[..^1], frames[1]);
    }

    private static IntegrityTerminatorMessageFramer CreateFramer(CrcOptions options) =>
        new(options.TermChar, new Crc16MessageIntegrity(options));

    private static byte[] FindPayload(CrcOptions options, Func<byte[], bool> predicate)
    {
        for (var value = 0; value <= ushort.MaxValue; value++)
        {
            var payload = new byte[] { (byte)value, (byte)(value >> 8) };
            if (payload.Contains(options.TermChar)) continue;
            var wire = new Crc(options).Append(payload);
            if (predicate(wire)) return payload;
        }

        throw new InvalidOperationException("Unable to synthesize requested CRC edge case.");
    }
}
