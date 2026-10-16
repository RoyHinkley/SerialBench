using SerialPortTerminal.Application;

namespace SerialPortTerminal.Tests;

public sealed class SendDataParserTests
{
    [Fact]
    public void Text_and_escapes_produce_exact_bytes()
    {
        Assert.True(SendDataParser.TryParse(@"A\x00\x03\\\r\n\t\377", out var bytes, out var error), error);
        Assert.Equal(new byte[] { 0x41, 0x00, 0x03, 0x5C, 0x0D, 0x0A, 0x09, 0xFF }, bytes);
    }

    [Fact]
    public void Escaping_and_parsing_round_trip_every_byte_value()
    {
        var original = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var expression = SendDataParser.Escape(original);

        Assert.True(SendDataParser.TryParse(expression, out var parsed, out var error), error);
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(@"\x")]
    [InlineData(@"\x0")]
    [InlineData(@"\xGG")]
    [InlineData(@"\400")]
    [InlineData(@"\q")]
    public void Invalid_escape_rejects_entire_expression(string expression)
    {
        Assert.False(SendDataParser.TryParse(expression, out var bytes, out var error));
        Assert.Empty(bytes);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
