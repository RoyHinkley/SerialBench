namespace SerialPortTerminal.Serial;

public sealed class CrcOptions
{
    public ushort InitialValue { get; set; } = 0xFFFF;
    public ushort Polynomial { get; set; } = 0xDAAE;
    public ushort ExpectedResidue { get; set; } = 0x82C0;
    public bool PostInvert { get; set; } = true;
    public bool MsBitFirst { get; set; }
    public bool MsByteFirst { get; set; }
    public byte TermChar { get; set; } = 0x03;
    public bool OmitTermChar { get; set; }
    public CrcOptions() { }
    public CrcOptions(ushort initialValue, ushort polynomial, ushort expectedResidue, bool postInvert, bool msBitFirst, bool msByteFirst, byte termChar, bool omitTermChar)
    { InitialValue = initialValue; Polynomial = polynomial; ExpectedResidue = expectedResidue; PostInvert = postInvert; MsBitFirst = msBitFirst; MsByteFirst = msByteFirst; TermChar = termChar; OmitTermChar = omitTermChar; }
}

/// <summary>Incremental 16-bit CRC implementation used by the serial protocols.</summary>
/// <remarks>CRC state is intentionally incremental because some legacy framing logic validates it as bytes arrive.</remarks>
public sealed class Crc
{
    private CrcOptions options;
    private ushort remainder;
    public CrcOptions Options { get => options; set { options = value ?? throw new ArgumentNullException(nameof(value)); Init(); } }
    public ushort Code => remainder;
    public Crc() : this(new CrcOptions()) { }
    public Crc(CrcOptions options) { this.options = options ?? throw new ArgumentNullException(nameof(options)); Init(); }
    public Crc(string value) : this() { Update(value); }
    public void Init() => remainder = Options.InitialValue;
    public bool Good() => remainder == Options.ExpectedResidue;
    public ushort Update(byte value)
    {
        if (Options.MsBitFirst)
        {
            remainder ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) { var msb = remainder & 0x8000; remainder <<= 1; if (msb != 0) remainder ^= Options.Polynomial; }
        }
        else
        {
            remainder ^= value;
            for (var bit = 0; bit < 8; bit++) { var lsb = remainder & 1; remainder >>= 1; if (lsb != 0) remainder ^= Options.Polynomial; }
        }
        return remainder;
    }
    public ushort Update(byte[] values, int count) { ArgumentNullException.ThrowIfNull(values); if ((uint)count > (uint)values.Length) throw new ArgumentOutOfRangeException(nameof(count)); for (var i = 0; i < count; i++) Update(values[i]); return remainder; }
    public ushort Update(byte[] values) => Update(values, values.Length);
    public ushort Update(ReadOnlySpan<byte> values) { foreach (var value in values) Update(value); return remainder; }
    public ushort Update(char[] values, int start, int count) { ArgumentNullException.ThrowIfNull(values); ArgumentOutOfRangeException.ThrowIfNegative(start); ArgumentOutOfRangeException.ThrowIfNegative(count); if (start > values.Length - count) throw new ArgumentOutOfRangeException(nameof(count)); for (var i = start; i < start + count; i++) Update(values[i]); return remainder; }
    public ushort Update(char[] values, int count) => Update(values, 0, count);
    public ushort Update(char[] values) => Update(values, values.Length);
    public ushort Update(char value) => Update((byte)value);
    public ushort Update(string value) { ArgumentNullException.ThrowIfNull(value); foreach (var c in value) Update(c); return remainder; }

    /// <summary>Constructs one complete codeword from exact payload bytes.</summary>
    public byte[] Append(ReadOnlySpan<byte> payload)
    {
        Init(); Update(payload);
        var checkCode = Options.PostInvert ? (ushort)~remainder : remainder;
        var result = new byte[payload.Length + (Options.OmitTermChar ? 2 : 3)];
        payload.CopyTo(result);
        var i = payload.Length;
        var crcBytes = BitConverter.GetBytes(checkCode);
        if (Options.MsByteFirst) { result[i++] = crcBytes[1]; result[i++] = crcBytes[0]; }
        else { result[i++] = crcBytes[0]; result[i++] = crcBytes[1]; }
        if (!Options.OmitTermChar) result[i] = Options.TermChar;
        return result;
    }

    public byte[] Append(byte[] payload) { ArgumentNullException.ThrowIfNull(payload); return Append(payload.AsSpan()); }
    public byte[] Append(string value) { ArgumentNullException.ThrowIfNull(value); var bytes = new byte[value.Length]; for (var i = 0; i < value.Length; i++) bytes[i] = (byte)value[i]; return Append(bytes); }
    public string AppendToString(string value) { var bytes = Append(value); return string.Create(bytes.Length, bytes, static (chars, source) => { for (var i = 0; i < source.Length; i++) chars[i] = (char)source[i]; }); }
    public static string AppendTo(string value, CrcOptions options) => new Crc(options).AppendToString(value);
    public static string AppendTo(string value) => AppendTo(value, new CrcOptions());
    public static byte[] Codeword(string value, CrcOptions options) => new Crc(options).Append(value);
    public static byte[] Codeword(string value) => Codeword(value, new CrcOptions());
}