using SerialCommunications.Serial;

namespace SerialBench.Tests;

public sealed class SerialProtocolSettingsTests
{
    [Fact]
    public void Snapshot_is_independent_of_mutable_crc_options()
    {
        var options = new CrcOptions(0xFFFF, 0xDAAE, 0x82C0, true, false, false, 0x03, false);
        var snapshot = SerialProtocolSettings.From(options, suppressInvalidMessages: true);

        options.Polynomial = 0xA001;
        options.TermChar = 0x04;
        options.OmitTermChar = true;

        Assert.Equal((ushort)0xDAAE, snapshot.Polynomial);
        Assert.Equal((byte)0x03, snapshot.TermChar);
        Assert.False(snapshot.OmitTermChar);
        Assert.True(snapshot.SuppressInvalidMessages);
    }

    [Fact]
    public void Each_crc_options_instance_created_from_snapshot_is_independent()
    {
        var snapshot = SerialProtocolSettings.From(new CrcOptions(), suppressInvalidMessages: false);
        var first = snapshot.CreateCrcOptions()!;
        var second = snapshot.CreateCrcOptions()!;

        first.Polynomial = 0xA001;

        Assert.NotSame(first, second);
        Assert.Equal((ushort)0xDAAE, second.Polynomial);
    }

    [Fact]
    public void Crc_parameter_changes_keep_same_receive_framing_strategy()
    {
        var current = SerialProtocolSettings.From(new CrcOptions(), suppressInvalidMessages: false);
        var changed = current with { Polynomial = 0xA001, ExpectedResidue = 0, SuppressInvalidMessages = true };

        Assert.True(current.UsesSameFramingStrategyAs(changed));
    }

    [Fact]
    public void Changing_between_term_and_silence_framing_changes_receive_strategy()
    {
        var terminated = SerialProtocolSettings.From(new CrcOptions { OmitTermChar = false }, suppressInvalidMessages: false);
        var silence = terminated with { OmitTermChar = true };

        Assert.False(terminated.UsesSameFramingStrategyAs(silence));
    }

    [Fact]
    public void Enabling_or_disabling_crc_changes_receive_strategy()
    {
        var crc = SerialProtocolSettings.From(new CrcOptions(), suppressInvalidMessages: false);
        var noCrc = SerialProtocolSettings.From(null, suppressInvalidMessages: false);

        Assert.False(crc.UsesSameFramingStrategyAs(noCrc));
    }
}
