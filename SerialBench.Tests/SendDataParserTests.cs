using SerialBench.Application;

namespace SerialBench.Tests;

public sealed class ByteExpressionTests
{
    [Fact]
    public void Text_and_escapes_produce_exact_bytes()
    {
        Assert.True(ByteExpression.TryParse(@"A\x00\x03\\\r\n\t", out var bytes, out var error), error);
        Assert.Equal(new byte[] { 0x41, 0x00, 0x03, 0x5C, 0x0D, 0x0A, 0x09 }, bytes);
    }

    [Fact]
    public void Formatting_and_parsing_round_trip_every_byte_value()
    {
        var original = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var expression = ByteExpression.Format(original);

        Assert.True(ByteExpression.TryParse(expression, out var parsed, out var error), error);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Formatting_uses_one_canonical_representation()
    {
        Assert.Equal(@"A\\\r\n\t\x00\x7F\xFF", ByteExpression.Format([0x41, 0x5C, 0x0D, 0x0A, 0x09, 0x00, 0x7F, 0xFF]));
    }

    [Fact]
    public void Literal_text_is_ascii_only()
    {
        Assert.False(ByteExpression.TryParse("é", out var bytes, out var error));
        Assert.Empty(bytes);
        Assert.Contains("not ASCII", error);

        Assert.True(ByteExpression.TryParse(@"\xE9", out bytes, out error), error);
        Assert.Equal(new byte[] { 0xE9 }, bytes);
    }

    [Theory]
    [InlineData(@"\x")]
    [InlineData(@"\x0")]
    [InlineData(@"\xGG")]
    [InlineData(@"\377")]
    [InlineData(@"\q")]
    public void Invalid_escape_rejects_entire_expression(string expression)
    {
        Assert.False(ByteExpression.TryParse(expression, out var bytes, out var error));
        Assert.Empty(bytes);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
