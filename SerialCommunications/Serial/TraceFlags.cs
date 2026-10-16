namespace SerialCommunications.Serial;

/// <summary>Advisory characteristics attached to a human-readable serial diagnostic trace.</summary>
[Flags]
public enum TraceFlags
{
    None = 0,
    Detail = 1,
    Noisy = 2
}
