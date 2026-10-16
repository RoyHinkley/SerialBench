namespace SerialPortTerminal.Serial;

/// <summary>Controls automatic attempts to restore a requested serial connection after transport loss.</summary>
/// <param name="RetryInterval">Delay between recovery attempts.</param>
/// <param name="MaximumAttempts">Maximum attempts before giving up; -1 means unlimited.</param>
/// <param name="MaximumDuration">Maximum recovery duration; <see langword="null"/> means unlimited.</param>
public sealed record SerialRecoveryPolicy(
    TimeSpan RetryInterval,
    int MaximumAttempts = -1,
    TimeSpan? MaximumDuration = null)
{
    public static SerialRecoveryPolicy Default { get; } = new(TimeSpan.FromSeconds(1));

    public bool AllowsAttempt(int attemptsAlreadyMade, TimeSpan elapsed)
    {
        if (attemptsAlreadyMade < 0) throw new ArgumentOutOfRangeException(nameof(attemptsAlreadyMade));
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (RetryInterval <= TimeSpan.Zero) throw new InvalidOperationException("RetryInterval must be greater than zero.");
        if (MaximumAttempts < -1) throw new InvalidOperationException("MaximumAttempts must be -1 or greater.");
        if (MaximumDuration is { } duration && duration < TimeSpan.Zero) throw new InvalidOperationException("MaximumDuration cannot be negative.");

        return (MaximumAttempts < 0 || attemptsAlreadyMade < MaximumAttempts)
            && (MaximumDuration is null || elapsed < MaximumDuration.Value);
    }
}
