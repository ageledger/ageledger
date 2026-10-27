namespace Ageledger;

/// <summary>
/// Everything is off by default: with <see cref="Enabled"/> = false the service makes no platform call,
/// shows nothing and logs nothing. <see cref="AgeSignalsService"/> copies these values when it is created; changes afterwards are ignored (build a new service).
/// </summary>
public sealed class AgeSignalsOptions
{
    /// <summary>Highest allowed Apple age gate (keeps every range label to two digits).</summary>
    public const int MaxAgeGate = 99;

    /// <summary>
    /// True when the gates are one to three ages, the first at least 2, none above <see cref="MaxAgeGate"/> (99) and each at least 2 above the one before,
    /// so every range label the platform can return (such as "0-12" or "13-15") has a lower number below the upper one and passes the decision log's checks.
    /// </summary>
    internal static bool AreValidAgeGates(IReadOnlyList<int>? gates)
    {
        if (gates is null || gates.Count is < 1 or > 3) return false;
        if (gates[0] < 2) return false;
        for (var i = 0; i < gates.Count; i++)
        {
            if (gates[i] > MaxAgeGate) return false;
            if (i > 0 && gates[i] - gates[i - 1] < 2) return false;
        }
        return true;
    }

    internal const string AgeGatesRule = "Age gates must be one to three ascending ages from 2 to 99, each at least 2 above the one before.";

    /// <summary>Longest allowed <see cref="RequestTimeout"/>.</summary>
    public static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromMinutes(10);

    // The service works on a private copy, so later changes to the caller's instance (or to its AgeGates array) have no effect.
    // AgeGates is copied to an array even when null or invalid (a disabled service never reads it; an enabled one validates the copy).
    internal AgeSignalsOptions Snapshot() => new()
    {
        Enabled = Enabled,
        AgeGates = AgeGates is null ? [] : [.. AgeGates],
        MinimumAge = MinimumAge,
        AdultAge = AdultAge,
        NoSignalDecision = NoSignalDecision,
        VerificationRequiredDecision = VerificationRequiredDecision,
        SharedRangePolicy = SharedRangePolicy,
        AgeBucketLogging = AgeBucketLogging,
        AppVersion = AppVersion,
        RuleId = RuleId,
        RequestTimeout = RequestTimeout,
    };

    /// <summary>Master feature flag. Default false.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Apple age gates (one to three ascending ages, each 2 to 99 and at least 2 above the one before), for example 13, 16, 18. Google Play's bands are set in Play Console instead.
    /// </summary>
    public IReadOnlyList<int> AgeGates { get; set; } = [13, 16, 18];

    /// <summary>
    /// The default policy blocks when the reported range's lower bound (null counts as 0) is below this age, so a range that
    /// may include someone younger is treated as the younger group. Default 13.
    /// </summary>
    public int MinimumAge { get; set; } = 13;

    /// <summary>
    /// The default policy asks for parental consent when the lower bound is at or above <see cref="MinimumAge"/> but below this age,
    /// and allows only when the lower bound is at or above it. Default 18.
    /// </summary>
    public int AdultAge { get; set; } = 18;

    /// <summary>Decision when the person did not share a range or no signal is available. Default NotApplicable.</summary>
    public AgeDecision NoSignalDecision { get; set; } = AgeDecision.NotApplicable;

    /// <summary>Decision when Google Play says verification is required. Default Blocked. The app owner must review this default.</summary>
    public AgeDecision VerificationRequiredDecision { get; set; } = AgeDecision.Blocked;

    /// <summary>
    /// Replaces the built-in policy for a shared range. It can run for different calls at the same time, so it must be thread-safe and free of side effects. It must not call <see cref="AgeSignalsService.EvaluateAsync"/> on the same service (not supported: it would recurse without end). If it throws, the exception propagates out of <see cref="AgeSignalsService.EvaluateAsync"/> and nothing is logged.
    /// If it returns a value that is not a defined <see cref="AgeDecision"/>, <see cref="AgeSignalsService.EvaluateAsync"/> throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public Func<AgeRangeBucket, AgeDecision>? SharedRangePolicy { get; set; }

    /// <summary>
    /// What the decision log stores in <c>ageBucket</c>. Default <see cref="AgeBucketLogging.Exact"/>. The app chooses; the library does not pick a value for any
    /// jurisdiction. Decisions are still logged, and the decision itself can imply the age group. The service copies its options when it is created, so changing this afterwards has no effect on that service.
    /// </summary>
    public AgeBucketLogging AgeBucketLogging { get; set; } = AgeBucketLogging.Exact;

    /// <summary>
    /// Longest time <see cref="AgeSignalsService.EvaluateAsync"/> waits for the platform call. Default null = no limit. When set it must be more than zero and at most
    /// <see cref="MaxRequestTimeout"/> (10 minutes), checked when an enabled service is created. When it expires the result is <see cref="AgeSignalsState.Error"/> with
    /// <see cref="AgeSignalsError.Timeout"/> and the decision is logged as usual. Apple's sheet and Google Play's consent screen can wait for the person indefinitely,
    /// so pass a token with a timeout or set this. The timeout also bounds the wait for an earlier request still in progress on the same service: a call queued behind a request that never completes ends in Timeout too.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>App version written to the decision log, for example "1.4.2" or "1.4.2-beta1". Checked when an enabled <see cref="AgeSignalsService"/> is created.</summary>
    public string AppVersion { get; set; } = "0.0.0";

    /// <summary>Your own short label for the rule the decision was made under, for example "gate-v1" or "gate-v2". Not a person identifier.</summary>
    public string RuleId { get; set; } = "unspecified";
}
