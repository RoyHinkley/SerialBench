using SerialBench.Application;
using SerialCommunications.Serial;

namespace SerialBench.Tests;

public sealed class TerminalConfigurationTests
{
    [Fact]
    public void DefaultProtocolPreservesHistoricalWireBehavior()
    {
        var configuration = new TerminalConfiguration();

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
        Assert.Equal(FrameLayout.PCE, protocol.Layout);
        Assert.Equal(MessageCheckKind.Crc16, protocol.CheckKind);
        Assert.Empty(protocol.Start);
        Assert.Equal([0x03], protocol.End);
        Assert.Equal((ushort)0xDAAE, protocol.Polynomial);
        Assert.Equal((ushort)0xFFFF, protocol.InitialValue);
        Assert.Equal((ushort)0x82C0, protocol.ExpectedResidue);
        Assert.True(protocol.PostInvert);
    }

    [Theory]
    [InlineData(FrameLayout.P)]
    [InlineData(FrameLayout.PE)]
    [InlineData(FrameLayout.PC)]
    [InlineData(FrameLayout.PEC)]
    [InlineData(FrameLayout.SP)]
    [InlineData(FrameLayout.SPE)]
    [InlineData(FrameLayout.SPC)]
    [InlineData(FrameLayout.SPEC)]
    [InlineData(FrameLayout.PCE)]
    public void EveryLibraryFrameLayoutCanBeConfigured(FrameLayout layout)
    {
        var configuration = new TerminalConfiguration
        {
            FrameLayout = layout,
            StartExpression = @"\x02ST",
            EndExpression = @"\r\n",
            CheckKind = LayoutHasCheck(layout) ? MessageCheckKind.Crc16 : MessageCheckKind.None
        };

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
        Assert.Equal(layout, protocol.Layout);
        Assert.Equal(new byte[] { 0x02, (byte)'S', (byte)'T' }, protocol.Start);
        Assert.Equal(new byte[] { 0x0D, 0x0A }, protocol.End);
        Assert.Equal(configuration.CheckKind, protocol.CheckKind);
    }

    [Fact]
    public void CrcConfigurationCanRemainDormantInLayoutWithoutCheckElement()
    {
        var configuration = new TerminalConfiguration { FrameLayout = FrameLayout.SPE, CheckKind = MessageCheckKind.Crc16 };

        Assert.True(configuration.TryCreateProtocolSettings(out var protocol, out var error), error);
        Assert.NotNull(protocol);
        Assert.Equal(MessageCheckKind.Crc16, protocol.CheckKind);
        Assert.False(protocol.HasCheckCode);
    }

    [Fact]
    public void LayoutContainingCheckRequiresConfiguredCheckMethod()
    {
        var configuration = new TerminalConfiguration { FrameLayout = FrameLayout.SPEC, CheckKind = MessageCheckKind.None };

        Assert.False(configuration.TryCreateProtocolSettings(out var protocol, out var error));
        Assert.Null(protocol);
        Assert.Contains("requires a message check", error);
    }

    [Fact]
    public void InvalidStartOrEndExpressionRejectsProtocolSnapshot()
    {
        var configuration = new TerminalConfiguration { StartExpression = @"\377" };
        Assert.False(configuration.TryCreateProtocolSettings(out var protocol, out var error));
        Assert.Null(protocol);
        Assert.Contains("start sequence", error);

        configuration.StartExpression = string.Empty;
        configuration.EndExpression = @"\q";
        Assert.False(configuration.TryCreateProtocolSettings(out protocol, out error));
        Assert.Null(protocol);
        Assert.Contains("end sequence", error);
    }

    private static bool LayoutHasCheck(FrameLayout layout) => layout is FrameLayout.PC or FrameLayout.PEC or FrameLayout.SPC or FrameLayout.SPEC or FrameLayout.PCE;
}