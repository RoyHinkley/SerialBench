using System.Text;

namespace SerialBench.Application;

/// <summary>Identifies the physical record types stored in a SerialBench session file.</summary>
public enum SessionRecordType : byte
{
    TxMessage = 1,
    RxMessage = 2,
    Diagnostic = 3,
}

/// <summary>Describes the receive check result recorded with an Rx message.</summary>
public enum SessionCheckValidity : byte
{
    NoCheck = 0,
    Valid = 1,
    Invalid = 2,
}

/// <summary>Base type for records read from a <see cref="SessionStore"/>.</summary>
public abstract record SessionRecord(SessionRecordType Type, DateTime TimestampUtc);

/// <summary>A transmitted message, decomposed into framing prefix, payload, and suffix.</summary>
public sealed record TxSessionRecord(DateTime TimestampUtc, byte[] Prefix, byte[] Payload, byte[] Suffix)
    : SessionRecord(SessionRecordType.TxMessage, TimestampUtc);

/// <summary>A received message, decomposed into framing prefix, payload, and suffix.</summary>
public sealed record RxSessionRecord(
    DateTime TimestampUtc,
    SessionCheckValidity CheckValidity,
    byte[] Prefix,
    byte[] Payload,
    byte[] Suffix)
    : SessionRecord(SessionRecordType.RxMessage, TimestampUtc);

/// <summary>A diagnostic message recorded exactly as supplied.</summary>
public sealed record DiagnosticSessionRecord(DateTime TimestampUtc, string Text)
    : SessionRecord(SessionRecordType.Diagnostic, TimestampUtc);

/// <summary>
/// Append-only SerialBench session store. The on-disk format is explicitly little-endian and is
/// recoverable by scanning forward to the first invalid or incomplete record.
/// </summary>
public sealed class SessionStore : IDisposable
{
    private static readonly byte[] Magic = "SBSS"u8.ToArray();
    private const byte MajorVersion = 1;
    private const byte MinorVersion = 0;
    private const int FileHeaderLength = 6;
    private const int CommonRecordLength = 13;
    private const int TxFixedLength = 19;
    private const int RxFixedLength = 20;

    private readonly FileStream stream;
    private readonly BinaryReader reader;
    private readonly BinaryWriter writer;
    private readonly List<long> eventOffsets = [];
    private readonly List<int> rxMessageEvents = [];
    private bool disposed;

    /// <summary>Gets the file offsets of all valid records in chronological order.</summary>
    public IReadOnlyList<long> EventOffsets => eventOffsets;

    /// <summary>Gets event ordinals for received-message records.</summary>
    public IReadOnlyList<int> RxMessageEvents => rxMessageEvents;

    /// <summary>Gets the number of valid records in the session.</summary>
    public int EventCount => eventOffsets.Count;

    /// <summary>Gets the number of received-message records in the session.</summary>
    public int RxMessageCount => rxMessageEvents.Count;

    /// <summary>Opens an existing session or creates a new version 1.0 session.</summary>
    public SessionStore(string path)
    {
        stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);
        reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        if (stream.Length == 0)
        {
            WriteFileHeader();
            writer.Flush();
        }
        else
        {
            ReadFileHeader();
        }

        ScanAndRecover();
    }

    /// <summary>Appends a transmitted message and returns its event ordinal.</summary>
    public int AppendTxMessage(DateTime timestampUtc, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> suffix)
    {
        ValidateTimestamp(timestampUtc);
        ValidateSegments(prefix, payload, suffix);
        var recordLength = checked(TxFixedLength + prefix.Length + payload.Length + suffix.Length);
        var eventOrdinal = BeginRecord(SessionRecordType.TxMessage, recordLength, timestampUtc);
        writer.Write((byte)prefix.Length);
        writer.Write(payload.Length);
        writer.Write((byte)suffix.Length);
        writer.Write(prefix);
        writer.Write(payload);
        writer.Write(suffix);
        return eventOrdinal;
    }

    /// <summary>Appends a received message and returns its event ordinal.</summary>
    public int AppendRxMessage(
        DateTime timestampUtc,
        SessionCheckValidity checkValidity,
        ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> suffix)
    {
        ValidateTimestamp(timestampUtc);
        ValidateCheckValidity(checkValidity);
        ValidateSegments(prefix, payload, suffix);
        var recordLength = checked(RxFixedLength + prefix.Length + payload.Length + suffix.Length);
        var eventOrdinal = BeginRecord(SessionRecordType.RxMessage, recordLength, timestampUtc);
        writer.Write((byte)checkValidity);
        writer.Write((byte)prefix.Length);
        writer.Write(payload.Length);
        writer.Write((byte)suffix.Length);
        writer.Write(prefix);
        writer.Write(payload);
        writer.Write(suffix);
        rxMessageEvents.Add(eventOrdinal);
        return eventOrdinal;
    }

    /// <summary>Appends a UTF-8 diagnostic exactly as supplied and returns its event ordinal.</summary>
    public int AppendDiagnostic(DateTime timestampUtc, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateTimestamp(timestampUtc);
        var bytes = Encoding.UTF8.GetBytes(text);
        var recordLength = checked(CommonRecordLength + bytes.Length);
        var eventOrdinal = BeginRecord(SessionRecordType.Diagnostic, recordLength, timestampUtc);
        writer.Write(bytes);
        return eventOrdinal;
    }

    /// <summary>Reads an event by chronological ordinal.</summary>
    public SessionRecord ReadEvent(int eventOrdinal)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if ((uint)eventOrdinal >= (uint)eventOffsets.Count) throw new ArgumentOutOfRangeException(nameof(eventOrdinal));

        writer.Flush();
        stream.Position = eventOffsets[eventOrdinal];
        var type = (SessionRecordType)reader.ReadByte();
        var recordLength = reader.ReadInt32();
        var timestamp = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);

        return type switch
        {
            SessionRecordType.TxMessage => ReadTx(timestamp),
            SessionRecordType.RxMessage => ReadRx(timestamp),
            SessionRecordType.Diagnostic => ReadDiagnostic(timestamp, recordLength),
            _ => throw new InvalidDataException($"Invalid session record type {(byte)type}.")
        };
    }

    /// <summary>Reads a received message by its received-message ordinal.</summary>
    public RxSessionRecord ReadRxMessage(int rxMessageOrdinal)
    {
        if ((uint)rxMessageOrdinal >= (uint)rxMessageEvents.Count) throw new ArgumentOutOfRangeException(nameof(rxMessageOrdinal));
        return (RxSessionRecord)ReadEvent(rxMessageEvents[rxMessageOrdinal]);
    }

    /// <summary>Flushes buffered session data to the operating system.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        writer.Flush();
    }

    /// <summary>Flushes buffered session data through to durable storage.</summary>
    public void FlushToDisk()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private int BeginRecord(SessionRecordType type, int recordLength, DateTime timestampUtc)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        writer.Flush();
        stream.Position = stream.Length;
        var eventOrdinal = eventOffsets.Count;
        eventOffsets.Add(stream.Position);
        writer.Write((byte)type);
        writer.Write(recordLength);
        writer.Write(timestampUtc.Ticks);
        return eventOrdinal;
    }

    private void WriteFileHeader()
    {
        writer.Write(Magic);
        writer.Write(MajorVersion);
        writer.Write(MinorVersion);
    }

    private void ReadFileHeader()
    {
        if (stream.Length < FileHeaderLength) throw new InvalidDataException("Incomplete SerialBench session header.");
        stream.Position = 0;
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a SerialBench session file.");
        var major = reader.ReadByte();
        var minor = reader.ReadByte();
        if (major != MajorVersion || minor > MinorVersion)
            throw new InvalidDataException($"Unsupported SerialBench session format {major}.{minor}.");
    }

    private void ScanAndRecover()
    {
        var offset = (long)FileHeaderLength;
        var validEnd = offset;

        while (offset < stream.Length)
        {
            var remaining = stream.Length - offset;
            if (remaining < CommonRecordLength) break;

            stream.Position = offset;
            var typeValue = reader.ReadByte();
            if (!Enum.IsDefined(typeof(SessionRecordType), typeValue)) break;
            var type = (SessionRecordType)typeValue;
            var recordLength = reader.ReadInt32();
            var minimumLength = type switch
            {
                SessionRecordType.TxMessage => TxFixedLength,
                SessionRecordType.RxMessage => RxFixedLength,
                SessionRecordType.Diagnostic => CommonRecordLength,
                _ => int.MaxValue,
            };
            if (recordLength < minimumLength || recordLength > remaining) break;

            _ = reader.ReadInt64();
            if (!ValidateRecordStructure(type, recordLength)) break;

            var eventOrdinal = eventOffsets.Count;
            eventOffsets.Add(offset);
            if (type == SessionRecordType.RxMessage) rxMessageEvents.Add(eventOrdinal);
            offset += recordLength;
            validEnd = offset;
        }

        if (validEnd != stream.Length) stream.SetLength(validEnd);
        stream.Position = stream.Length;
    }

    private bool ValidateRecordStructure(SessionRecordType type, int recordLength)
    {
        if (type == SessionRecordType.Diagnostic) return true;

        if (type == SessionRecordType.RxMessage)
        {
            var checkValidity = (SessionCheckValidity)reader.ReadByte();
            if (!IsValidCheckValidity(checkValidity)) return false;
        }

        var prefixLength = reader.ReadByte();
        var payloadLength = reader.ReadInt32();
        var suffixLength = reader.ReadByte();
        if (payloadLength < 0) return false;

        var fixedLength = type == SessionRecordType.TxMessage ? TxFixedLength : RxFixedLength;
        var expectedLength = (long)fixedLength + prefixLength + payloadLength + suffixLength;
        return expectedLength == recordLength;
    }

    private TxSessionRecord ReadTx(DateTime timestamp)
    {
        var prefixLength = reader.ReadByte();
        var payloadLength = reader.ReadInt32();
        var suffixLength = reader.ReadByte();
        return new TxSessionRecord(timestamp, ReadBytes(prefixLength), ReadBytes(payloadLength), ReadBytes(suffixLength));
    }

    private RxSessionRecord ReadRx(DateTime timestamp)
    {
        var checkValidity = (SessionCheckValidity)reader.ReadByte();
        var prefixLength = reader.ReadByte();
        var payloadLength = reader.ReadInt32();
        var suffixLength = reader.ReadByte();
        return new RxSessionRecord(timestamp, checkValidity, ReadBytes(prefixLength), ReadBytes(payloadLength), ReadBytes(suffixLength));
    }

    private DiagnosticSessionRecord ReadDiagnostic(DateTime timestamp, int recordLength) =>
        new(timestamp, Encoding.UTF8.GetString(ReadBytes(recordLength - CommonRecordLength)));

    private byte[] ReadBytes(int count)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException();
        return bytes;
    }

    private static void ValidateTimestamp(DateTime timestampUtc)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Session timestamps must be UTC DateTime values.", nameof(timestampUtc));
    }

    private static void ValidateSegments(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> suffix)
    {
        if (prefix.Length > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(prefix), "Prefix length cannot exceed 255 bytes.");
        if (suffix.Length > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(suffix), "Suffix length cannot exceed 255 bytes.");
        _ = checked(TxFixedLength + prefix.Length + payload.Length + suffix.Length);
    }

    private static void ValidateCheckValidity(SessionCheckValidity value)
    {
        if (!IsValidCheckValidity(value)) throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static bool IsValidCheckValidity(SessionCheckValidity value) =>
        value is SessionCheckValidity.NoCheck or SessionCheckValidity.Valid or SessionCheckValidity.Invalid;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        writer.Flush();
        writer.Dispose();
        reader.Dispose();
        stream.Dispose();
    }
}
