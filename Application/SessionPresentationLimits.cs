namespace SerialBench.Application;

/// <summary>
/// Tunable working-set limits for bounded session presentation. These values affect only runtime
/// performance and presentation behavior; they are not part of the session-file format.
/// </summary>
internal static class SessionPresentationLimits
{
    /// <summary>Lower comfortable size after a window has been deliberately shifted or trimmed.</summary>
    public const int MinimumRecords = 250;

    /// <summary>Comfortable record count around which a shifted middle-of-session window is centered.</summary>
    public const int NominalRecords = 500;

    /// <summary>Maximum records retained before distant records should be trimmed.</summary>
    public const int MaximumRecords = 1000;

    /// <summary>Preferred number of records to fetch when extending a presentation window.</summary>
    public const int FetchRecords = 250;

    /// <summary>Distance from a staged edge at which additional history should be fetched.</summary>
    public const int EdgeClearanceRecords = 75;
}
