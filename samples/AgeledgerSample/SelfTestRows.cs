#if DEBUG
// This file is also compiled into tests/Ageledger.Tests (to count the cases), so it must reference library types only: nothing from MAUI or a platform.
using Ageledger;

namespace AgeledgerSample;

/// <summary>
/// What one case of a self-test row must show. A null field is not asserted (it is still recorded). Debug builds only.
/// </summary>
internal sealed record Expect(
    string Name,
    AgeSignalsState? State = null,
    string? Bucket = null,
    AgeDecision? Decision = null,
    bool? Logged = null,
    AgeSignalsError? Error = null,
    SignificantChange? Change = null,
    bool Asserted = true,   // false: recorded only; the case passes when it ran and returned a defined state
    string? Suite = null);   // null: every suite the row runs in; otherwise only this one (L3's write and read halves)

/// <summary>
/// One row of docs/device-test-plan.md that the self-test runs. <paramref name="Platform"/> is "both", "android", "ios",
/// "android-physical" or "android-virtual"; <paramref name="RunsIn"/> names the suites that run it.
/// </summary>
internal sealed record SelfTestRow(string Id, string Platform, string Source, string[] RunsIn, string Check, params Expect[] Cases);

/// <summary>
/// The one table of expected values for the self-test. The row ids must exist in docs/device-test-plan.md and the plan's
/// coverage table must list every row here (tests/Ageledger.Tests/SelfTestRowsTests.cs checks both). Made-up data only.
/// </summary>
internal static class SelfTestRows
{
    private static readonly string[] All = ["all"];
    private static readonly string[] RealPlay = ["real-play"];   // opt-in: calls the real Play service, which may show its own consent screen
    private const string Range = "range source"; // replaced by the platform's own label ("fake manager" or "fake backend")

    private static Expect Ok(AgeSignalsState state, string bucket, AgeDecision decision, SignificantChange change = SignificantChange.None, string name = "") =>
        new(name, state, bucket, decision, true, AgeSignalsError.None, change);

    private static Expect Err(string name, AgeSignalsState state, AgeSignalsError error) =>
        new(name, state, "unknown", AgeDecision.NotApplicable, true, error, SignificantChange.None);

    // An Android error code set on the access call, then on the check call.
    private static Expect[] Both(string code, AgeSignalsState state, AgeSignalsError error) =>
        [Err("access:" + code, state, error), Err("check:" + code, state, error)];

    public static readonly IReadOnlyList<SelfTestRow> Table =
    [
        new("A1", "android", "fake manager", All, "access SHARED, 18 and older", Ok(AgeSignalsState.Shared, "18+", AgeDecision.Allowed)),
        new("A2", "android", "fake manager", All, "access SHARED, 16-17", Ok(AgeSignalsState.Shared, "16-17", AgeDecision.AllowedWithParentalConsent)),
        new("A3", "android", "fake manager", All, "access SHARED, 13-15", Ok(AgeSignalsState.Shared, "13-15", AgeDecision.AllowedWithParentalConsent)),
        new("A4", "android", "fake manager", All, "access SHARED, 0-12", Ok(AgeSignalsState.Shared, "0-12", AgeDecision.Blocked)),
        new("A4b", "android", "fake manager", All, "access SHARED, 0-17 (younger end decides)", Ok(AgeSignalsState.Shared, "0-17", AgeDecision.Blocked)),
        new("A4c", "android", "fake manager", All, "A4b with MinimumAge 18", Ok(AgeSignalsState.Shared, "0-17", AgeDecision.Blocked)),
        new("A4d", "android", "fake manager", All, "access SHARED, 12-17 (lower 12 is below 13)", Ok(AgeSignalsState.Shared, "12-17", AgeDecision.Blocked)),
        new("A5", "android", "fake manager", All, "access SHARED, no bounds", Err("", AgeSignalsState.NotShared, AgeSignalsError.None)),
        new("A6", "android", "fake manager", All, "access NOT_SHARED", Err("", AgeSignalsState.NotShared, AgeSignalsError.None)),
        new("A7", "android", "fake manager", All, "access VERIFICATION_REQUIRED",
            new Expect("", AgeSignalsState.VerificationRequired, "unknown", AgeDecision.Blocked, true, AgeSignalsError.None, SignificantChange.None)),
        new("A8", "android", "fake manager", All, "13-15 plus significant change PENDING", Ok(AgeSignalsState.Shared, "13-15", AgeDecision.AllowedWithParentalConsent, SignificantChange.Pending)),
        new("A9", "android", "fake manager", All, "18 and older plus significant change APPROVED", Ok(AgeSignalsState.Shared, "18+", AgeDecision.Allowed, SignificantChange.Approved)),
        new("A10", "android", "fake manager", All, "NETWORK_ERROR on the access call, then on the check call (managed type names recorded)",
            [.. Both("NetworkError", AgeSignalsState.Error, AgeSignalsError.Network)]),
        new("A11", "android", "fake manager", All, "API_NOT_AVAILABLE on both calls",
            [.. Both("ApiNotAvailable", AgeSignalsState.Unavailable, AgeSignalsError.NotAvailable)]),
        new("A12", "android", "fake manager", All, "APP_NOT_OWNED on both calls",
            [.. Both("AppNotOwned", AgeSignalsState.Unavailable, AgeSignalsError.AppNotOwned)]),
        new("A13", "android", "fake manager", All, "Play components missing or outdated, on both calls",
            [.. new[] { "PlayStoreNotFound", "PlayServicesNotFound", "CannotBindToService", "PlayStoreVersionOutdated", "PlayServicesVersionOutdated", "SdkVersionOutdated" }
                .SelectMany(c => Both(c, AgeSignalsState.Unavailable, AgeSignalsError.PlatformComponentMissing))]),
        new("A14", "android", "fake manager", All, "CLIENT_TRANSIENT_ERROR and INTERNAL_ERROR, on both calls",
            [.. Both("ClientTransientError", AgeSignalsState.Error, AgeSignalsError.Transient), .. Both("InternalError", AgeSignalsState.Error, AgeSignalsError.Internal)]),
        new("A15", "android", "fake manager", All, "custom SharedRangePolicy returning Blocked, with 18 and older",
            new Expect("", AgeSignalsState.Shared, "18+", AgeDecision.Blocked, true, AgeSignalsError.None, SignificantChange.None)),
        new("A16", "android", "fake manager", All, "no activity available", Err("", AgeSignalsState.Error, AgeSignalsError.NoPresenter)),

        // Opt-in suite "real-play". Recorded, not asserted: the real manager's answer depends on the device and its Play setup. Pass = a defined state, no
        // exception, one log entry. Only the state and the error category are kept (never the bucket, decision or significant change).
        new("A20", "android-physical", "real manager", RealPlay, "sideloaded, real manager (record)", new Expect("", Asserted: false)),
        new("A21", "android-virtual", "real manager", RealPlay, "sideloaded, real manager, no Play Store image (record)", new Expect("", Asserted: false)),
        new("H2", "android", "fake manager", All, "Evaluate from a worker thread", Ok(AgeSignalsState.Shared, "18+", AgeDecision.Allowed)),

        new("L1", "both", Range, All, "export is a JSON array and the file is JSON Lines; every object has exactly the five fields", new Expect("")),
        new("L1b", "both", Range, All, "timestamps end in Z; a bucket such as 18+ has a literal plus, not an escape", new Expect("")),
        new("L2", "both", Range, All, "Clear, then Export gives [] and the file is gone", new Expect("")),
        new("L2b", "both", Range, All, "Clear removes leftover .tmp files and leaves unrelated files", new Expect("")),
        new("L3", "both", Range, ["l3-write", "l3-read"], "entries written before a kill are there after the relaunch (host script drives the kill)", new Expect("write", Suite: "l3-write"), new Expect("read", Suite: "l3-read")),
        new("L4", "both", Range, All, "1000 seeded entries plus one append leave 1000, the oldest dropped", new Expect("")),
        new("L5", "both", Range, All, "Enabled=false: Disabled, NotApplicable, no platform call, no log entry",
            new Expect("", AgeSignalsState.Disabled, null, AgeDecision.NotApplicable, false, AgeSignalsError.None)),
        new("L6", "both", Range, All, "two services evaluating at once leave two valid entries", new Expect("")),
        new("L7", "both", Range, All, "an unwritable log: result and decision still returned, Logged=false",
            Ok(AgeSignalsState.Shared, "13-15", AgeDecision.AllowedWithParentalConsent, name: "writable"),
            new Expect("unwritable", AgeSignalsState.Shared, "13-15", AgeDecision.AllowedWithParentalConsent, false, AgeSignalsError.None)),
        new("L8", "both", Range, All, "invalid settings throw ArgumentException only when Enabled",
            new Expect("invalid-gates-enabled"), new Expect("invalid-rule-enabled"), new Expect("invalid-version-enabled"),
            new Expect("invalid-disabled", AgeSignalsState.Disabled, null, AgeDecision.NotApplicable, false, AgeSignalsError.None)),
        new("L9", "both", Range, All, "valid gate sets construct; invalid ones are refused", new Expect("valid"), new Expect("invalid")),
        new("L10", "both", Range, All, "Exact logs 13-15, Withheld logs withheld; the screen value and the decision are the same",
            Ok(AgeSignalsState.Shared, "13-15", AgeDecision.AllowedWithParentalConsent)),
        new("L11", "both", Range, All, "Withheld with a Blocked result: the entry bucket is withheld and the decision Blocked (bucket shown is the logged one)",
            Ok(AgeSignalsState.Shared, "withheld", AgeDecision.Blocked)),
        new("L12", "both", Range, All, "0-17 with MinimumAge 18 is Blocked (younger end)", Ok(AgeSignalsState.Shared, "0-17", AgeDecision.Blocked)),

        new("L13", "both", Range, All, "the sample's saved settings and the manual log file are the same before and after the run", new Expect("")),
        new("8.1b", "both", Range, All, "pre-cancelled token: OperationCanceledException, no platform call, no log entry, short elapsed time", new Expect("")),
    ];

    /// <summary>
    /// The (row, case) pairs a run of <paramref name="suite"/> must produce on <paramref name="platform"/> ("android" or "ios"); the results file
    /// lists their number as expectedCases, and any pair that did not run is recorded as failed.
    /// </summary>
    public static IReadOnlyList<(SelfTestRow Row, Expect Case)> ExpectedCases(string suite, string platform, bool isVirtual) =>
        Table.Where(r => r.RunsIn.Contains(suite) && AppliesTo(r.Platform, platform, isVirtual))
            .SelectMany(r => r.Cases.Where(c => c.Suite is null || c.Suite == suite).Select(c => (r, c)))
            .ToList();

    private static bool AppliesTo(string rowPlatform, string platform, bool isVirtual) => rowPlatform switch
    {
        "both" => true,
        "android" => platform == "android",
        "ios" => platform == "ios",
        "android-physical" => platform == "android" && !isVirtual,
        "android-virtual" => platform == "android" && isVirtual,
        _ => false,
    };

    public static SelfTestRow? Find(string id) => Table.FirstOrDefault(r => r.Id == id);
}
#endif
