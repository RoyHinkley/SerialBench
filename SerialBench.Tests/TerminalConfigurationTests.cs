using SerialBench.Application;
using SerialCommunications.Serial;

namespace SerialBench.Tests;

public sealed class TerminalConfigurationTests
{
    [Fact]
    public void DefaultProtocolUsesCrcThenTerminationByte()
    {
        var configuration = new TerminalConfiguration();

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
        Assert.Equal(FrameLayout.PCE, protocol.Layout);
        Assert.Equal(MessageCheckKind.Crc16, protocol.CheckKind);
        Assert.Equal([0x03], protocol.End);
        Assert.Equal((ushort)0xDAAE, protocol.Polynomial);
        Assert.Equal((ushort)0xFFFF, protocol.InitialValue);
        Assert.Equal((ushort)0x82C0, protocol.ExpectedResidue);
        Assert.True(protocol.PostInvert);
    }

    [Fact]
    public void SilenceFramedCrcUsesPayloadThenCheck()
    {
        var configuration = new TerminalConfiguration { OmitTermChar = true };

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
        Assert.Equal(FrameLayout.PC, protocol.Layout);
        Assert.Equal(MessageCheckKind.Crc16, protocol.CheckKind);
        Assert.Empty(protocol.End);
    }

    [Fact]
    public void TerminatedProtocolWithoutCrcUsesPayloadThenEnd()
    {
        var configuration = new TerminalConfiguration { UseCrc = false, TermChar = "0D" };

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
        Assert.Equal(FrameLayout.PE, protocol.Layout);
        Assert.Equal(MessageCheckKind.None, protocol.CheckKind);
        Assert.Equal([0x0D], protocol.End);
    }

    [Fact]
    public void SilenceFramedProtocolWithoutCrcUsesPayloadOnly()
    {
        var configuration = new TerminalConfiguration { UseCrc = false, OmitTermChar = true };

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
        Assert.Equal(FrameLayout.P, protocol.Layout);
        Assert.Equal(MessageCheckKind.None, protocol.CheckKind);
        Assert.Empty(protocol.End);
    }

    [Fact]
    public void InvalidTerminationByteIsIgnoredWhenSilenceFramed()
    {
        var configuration = new TerminalConfiguration { TermChar = "not hex", OmitTermChar = true };

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
    }

    [Fact]
    public void InvalidTerminationByteIsRejectedWhenUsed()
    {
        var configuration = new TerminalConfiguration { TermChar = "not hex" };

        Assert.False(configuration.TryCreateProtocolSettings(out var protocol, out var error));
        Assert.Null(protocol);
        Assert.Contains("termination character", error);
    }
}