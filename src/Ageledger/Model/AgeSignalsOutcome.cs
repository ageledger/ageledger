namespace Ageledger;

/// <summary>
/// What the platform said, in a neutral shape. It deliberately has no install ID, no age-source tier,
/// no account or device identifier: the library never hands them to the app, so it cannot log them.
/// </summary>
public sealed record AgeSignalsOutcome
{
    /// <summary>The platform that produced the signal.</summary>
    public required AgeSignalsPlatform Platform { get; init; }
    /// <summary>What the platform told the app.</summary>
    public required AgeSignalsState State { get; init; }
    /// <summary>The reported age range; <see cref="AgeRangeBucket.Unknown"/> when none was shared.</summary>
    public AgeRangeBucket Bucket { get; init; } = AgeRangeBucket.Unknown;
    /// <summary>The neutral error category; <see cref="AgeSignalsError.None"/> when there was no error.</summary>
    public AgeSignalsError Error { get; init; } = AgeSignalsError.None;
    /// <summary>Google Play only.</summary>
    public SignificantChange SignificantChange { get; init; } = SignificantChange.None;
    /// <summary>Apple only (the parental controls reported with a shared range).</summary>
    public ParentalControls ParentalControls { get; init; } = ParentalControls.None;

    /// <summary>The result of a disabled service: no platform, state <see cref="AgeSignalsState.Disabled"/>.</summary>
    public static AgeSignalsOutcome Disabled { get; } =
        new() { Platform = AgeSignalsPlatform.None, State = AgeSignalsState.Disabled };
}

/// <summary>
/// A result plus the decision the app's policy made from it. <see cref="Logged"/> is true only when a decision log
/// is configured and the entry was written; it is false when no log is set, the service is disabled, or the write failed.
/// </summary>
public sealed record AgeSignalsEvaluation(AgeSignalsOutcome Result, AgeDecision Decision, bool Logged = false);
