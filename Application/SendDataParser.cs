using System.Globalization;
using SerialPortTerminal.Serial;

namespace SerialPortTerminal.Application;

/// <summary>Converts terminal send expressions to their exact wire bytes.</summary>
/// <remarks>
/// Ordinary characters use the terminal's one-byte Latin-1 representation. Backslash escapes
/// deliberately mark conversions from human-entered notation to bytes; no escape interpretation
/// belongs in the serial transport itself.
/// </remarks>
public static class SendDataParser
{
    public static bool TryParse(string expression, out byte[] bytes, out string? error)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var result = new List<byte>(expression.Length);
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c != '\\')
            {
                if (c > byte.MaxValue) { bytes = []; error = $"Character U+{(int)c:X4} at position {i + 1} cannot be represented as one byte."; return false; }
                result.Add((byte)c); continue;
            }
            var escapeStart = i;
            if (++i >= expression.Length) { bytes = []; error = $"Incomplete escape at position {escapeStart + 1}."; return false; }
            c = expression[i];
            switch (c)
            {
                case '\\': result.Add((byte)'\\'); break;
                case 'r': result.Add((byte)'\r'); break;
                case 'n': result.Add((byte)'\n'); break;
                case 't': result.Add((byte)'\t'); break;
                case 'x':
                case 'X':
                    if (i + 2 >= expression.Length || !byte.TryParse(expression.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    { bytes = []; error = $"Invalid hex escape at position {escapeStart + 1}; use \\xHH with exactly two hex digits."; return false; }
                    result.Add(hex); i += 2; break;
                default:
                    if (c is >= '0' and <= '7')
                    {
                        var end = i;
                        while (end + 1 < expression.Length && end - i < 2 && expression[end + 1] is >= '0' and <= '7') end++;
                        var octal = 0;
                        for (var j = i; j <= end; j++) octal = octal * 8 + expression[j] - '0';
                        if (octal > byte.MaxValue) { bytes = []; error = $"Octal escape at position {escapeStart + 1} exceeds one byte."; return false; }
                        result.Add((byte)octal); i = end; break;
                    }
                    bytes = []; error = $"Unknown escape \\{c} at position {escapeStart + 1}."; return false;
            }
        }
        bytes = result.ToArray(); error = null; return true;
    }

    /// <summary>Formats arbitrary bytes as an unambiguous terminal expression.</summary>
    public static string Escape(ReadOnlySpan<byte> bytes) => SerialDataFormatter.ToEscapedText(bytes);
}
