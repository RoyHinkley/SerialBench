using System.Globalization;
using System.Text;

namespace SerialBench.Application;

/// <summary>Converts between SerialBench byte expressions and exact byte sequences.</summary>
/// <remarks>
/// Printable ASCII is represented literally except backslash. Backslash, CR, LF, and TAB use the familiar
/// escapes \\, \r, \n, and \t; every other byte is represented as \xHH. This notation belongs to SerialBench's
/// interactive UI and deliberately does not leak into the serial communications layer.
/// </remarks>
public static class ByteExpression
{
    /// <summary>Parses a SerialBench byte expression into its exact byte sequence.</summary>
    public static bool TryParse(string expression, out byte[] bytes, out string? error)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var result = new List<byte>(expression.Length);
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c != '\\')
            {
                if (c > 0x7F)
                {
                    bytes = [];
                    error = $"Character U+{(int)c:X4} at position {i + 1} is not ASCII; use a byte escape such as \\xHH when an 8-bit value is required.";
                    return false;
                }
                result.Add((byte)c);
                continue;
            }

            var escapeStart = i;
            if (++i >= expression.Length)
            {
                bytes = [];
                error = $"Incomplete escape at position {escapeStart + 1}.";
                return false;
            }

            c = expression[i];
            switch (c)
            {
                case '\\': result.Add((byte)'\\'); break;
                case 'r': result.Add((byte)'\r'); break;
                case 'n': result.Add((byte)'\n'); break;
                case 't': result.Add((byte)'\t'); break;
                case 'x':
                case 'X':
                    if (i + 2 >= expression.Length ||
                        !byte.TryParse(expression.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    {
                        bytes = [];
                        error = $"Invalid hex escape at position {escapeStart + 1}; use \\xHH with exactly two hex digits.";
                        return false;
                    }
                    result.Add(hex);
                    i += 2;
                    break;
                default:
                    bytes = [];
                    error = $"Unknown escape \\{c} at position {escapeStart + 1}. Use \\xHH for arbitrary byte values.";
                    return false;
            }
        }

        bytes = result.ToArray();
        error = null;
        return true;
    }

    /// <summary>Formats arbitrary bytes as the canonical SerialBench byte expression.</summary>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            switch (value)
            {
                case (byte)'\\': result.Append("\\\\"); break;
                case (byte)'\r': result.Append("\\r"); break;
                case (byte)'\n': result.Append("\\n"); break;
                case (byte)'\t': result.Append("\\t"); break;
                case >= 0x20 and <= 0x7E: result.Append((char)value); break;
                default: result.Append($"\\x{value:X2}"); break;
            }
        }
        return result.ToString();
    }
}
