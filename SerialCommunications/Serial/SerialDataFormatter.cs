using System.Text;

namespace SerialPortTerminal.Serial;

/// <summary>
/// Formats serial data for human-facing diagnostic display and logging without changing the
/// underlying bytes.
/// </summary>
public static class SerialDataFormatter
{
    /// <summary>Formats arbitrary bytes as unambiguous text using the terminal escape syntax.</summary>
    public static string ToEscapedText(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'\\': result.Append("\\\\"); break;
                case (byte)'\r': result.Append("\\r"); break;
                case (byte)'\n': result.Append("\\n"); break;
                case (byte)'\t': result.Append("\\t"); break;
                default:
                    if (b is >= 0x20 and <= 0x7E) result.Append((char)b);
                    else result.Append($"\\x{b:X2}");
                    break;
            }
        }
        return result.ToString();
    }

    /// <summary>Formats a one-byte-per-character string as unambiguous text.</summary>
    public static string ToEscapedText(string? value) =>
        ToEscapedText(Encoding.Latin1.GetBytes(value ?? string.Empty));

    /// <summary>Formats each character as its low eight-bit hexadecimal byte value.</summary>
    public static string ToByteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return string.Empty;

        var result = new StringBuilder(value.Length * 3 - 1);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0) result.Append(' ');
            result.Append(((byte)value[i]).ToString("X2"));
        }
        return result.ToString();
    }

    /// <summary>Formats a byte buffer as two-digit hexadecimal bytes.</summary>
    public static string ToByteString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;

        var result = new StringBuilder(bytes.Length * 3 - 1);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0) result.Append(' ');
            result.Append(bytes[i].ToString("X2"));
        }
        return result.ToString();
    }
}
