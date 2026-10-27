#if DEBUG
using Android.App;
using Android.Gms.Extensions;
using Google.Android.Play.AgeSignals;
using Google.Android.Play.AgeSignals.Model;
using Google.Android.Play.AgeSignals.Testing;
using Ageledger;
using Platform = Microsoft.Maui.ApplicationModel.Platform;

namespace AgeledgerSample;

internal static partial class SelfTestPlatform
{
    public static partial ISelfTestPlatform Create() => new AndroidSelfTestPlatform();
}

/// <summary>Android self-test: Google's FakeAgeSignalsManager behind the real GoogleAgeSignalsBackend (rows A1-A16, H2), and the real manager for A20/A21.</summary>
internal sealed class AndroidSelfTestPlatform : ISelfTestPlatform
{
    private static Activity? CurrentActivity() => Platform.CurrentActivity;

    private static readonly (string Name, int Code)[] ErrorCodes =
    [
        ("NetworkError", AgeSignalsErrorCode.NetworkError), ("ApiNotAvailable", AgeSignalsErrorCode.ApiNotAvailable),
        ("AppNotOwned", AgeSignalsErrorCode.AppNotOwned), ("PlayStoreNotFound", AgeSignalsErrorCode.PlayStoreNotFound),
        ("PlayServicesNotFound", AgeSignalsErrorCode.PlayServicesNotFound), ("CannotBindToService", AgeSignalsErrorCode.CannotBindToService),
        ("PlayStoreVersionOutdated", AgeSignalsErrorCode.PlayStoreVersionOutdated),
        ("PlayServicesVersionOutdated", AgeSignalsErrorCode.PlayServicesVersionOutdated),
        ("SdkVersionOutdated", AgeSignalsErrorCode.SdkVersionOutdated),
        ("ClientTransientError", AgeSignalsErrorCode.ClientTransientError), ("InternalError", AgeSignalsErrorCode.InternalError),
    ];

    /// <summary>What to queue on a new fake: the access status, the range, an optional significant-change status and an optional error on one or both calls.</summary>
    private sealed record Setup(int Access, int? Lower, int? Upper, int? Change = null, int? ErrorCode = null, bool ErrorOnAccess = false, bool ErrorOnCheck = false);

    private static readonly (string Id, Setup Setup, Action<AgeSignalsOptions>? Tweak)[] Simple =
    [
        ("A1", new(AgeSignalsStatus.Shared, 18, null), null),
        ("A2", new(AgeSignalsStatus.Shared, 16, 17), null),
        ("A3", new(AgeSignalsStatus.Shared, 13, 15), null),
        ("A4", new(AgeSignalsStatus.Shared, 0, 12), null),
        ("A4b", new(AgeSignalsStatus.Shared, 0, 17), null),
        ("A4c", new(AgeSignalsStatus.Shared, 0, 17), o => o.MinimumAge = 18),
        ("A4d", new(AgeSignalsStatus.Shared, 12, 17), null),
        ("A5", new(AgeSignalsStatus.Shared, null, null), null),
        ("A6", new(AgeSignalsStatus.NotShared, null, null), null),
        ("A7", new(AgeSignalsStatus.VerificationRequired, null, null), null),
        ("A8", new(AgeSignalsStatus.Shared, 13, 15, SignificantChangeStatus.Pending), null),
        ("A9", new(AgeSignalsStatus.Shared, 18, null, SignificantChangeStatus.Approved), null),
        ("A15", new(AgeSignalsStatus.Shared, 18, null), o => o.SharedRangePolicy = _ => AgeDecision.Blocked),
    ];

    public string Name => "android";
    public string RangeSource => "fake manager";

    public IAgeSignalsProvider RangeProvider(int? lower, int? upper) =>
        ProviderFor(Make(new Setup(AgeSignalsStatus.Shared, lower, upper)), CurrentActivity);

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        // MAUI sets the current activity after OnCreate; the rows that need one read it from the main looper when they run.
        var until = DateTime.UtcNow.AddSeconds(20);
        while (Platform.CurrentActivity is null && DateTime.UtcNow < until) await Task.Delay(100, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunPlatformRowsAsync(SelfTestRun run)
    {
        foreach (var (id, setup, tweak) in Simple)
            await run.RunCase(id, "", () => run.EvalObs(id, ProviderFor(Make(setup), CurrentActivity), tweak));

        // A10-A14: each case in the table names the call ("access" or "check") and the error code to queue on it.
        foreach (var id in new[] { "A10", "A11", "A12", "A13", "A14" })
        {
            foreach (var expect in SelfTestRows.Find(id)!.Cases)
            {
                var parts = expect.Name.Split(':');
                var onAccess = parts[0] == "access";
                var code = ErrorCodes.First(e => e.Name == parts[1]).Code;
                var setup = new Setup(AgeSignalsStatus.Shared, 13, 15, null, code, onAccess, !onAccess);
                await run.RunCase(id, expect.Name, async () =>
                {
                    var obs = await run.EvalObs(id, ProviderFor(Make(setup), CurrentActivity));
                    obs.ExceptionType = await ManagedTypeNamesAsync(setup);   // type names only, never messages
                    return obs;
                });
            }
        }

        await run.RunCase("A16", "", () => run.EvalObs("A16", ProviderFor(Make(new Setup(AgeSignalsStatus.Shared, 13, 15)), () => null)));

        // H2: Evaluate from a worker thread; the backend moves the access request to the main looper.
        await run.RunCase("H2", "", async () =>
        {
            var provider = ProviderFor(Make(new Setup(AgeSignalsStatus.Shared, 18, null)), CurrentActivity);
            return Obs.From(await Task.Run(() => run.Eval(provider, run.NewLog("H2"))));
        });
    }

    /// <summary>
    /// The opt-in "real-play" suite: A20 (device) or A21 (emulator) with the real Play manager on a sideloaded build. It may show Play's own consent
    /// screen and its answer can describe a real account, so only the state, error category and Logged flag are kept; bucket, decision and
    /// significant change are dropped before anything is recorded.
    /// </summary>
    public async Task RunRealPlayRowsAsync(SelfTestRun run)
    {
        var realId = SelfTestRun.IsVirtualDevice ? "A21" : "A20";
        await run.RunCase(realId, "", async () =>
        {
            var provider = new AndroidAgeSignalsProvider(new GoogleAgeSignalsBackend(CurrentActivity));
            var ev = await run.Eval(provider, run.NewLog(realId), o => { o.RequestTimeout = TimeSpan.FromSeconds(15); o.AgeBucketLogging = AgeBucketLogging.Withheld; });   // the temporary log never holds a real bucket
            return new Obs { State = ev.Result.State, Error = ev.Result.Error, Logged = ev.Logged, Ok = ev.Result.State != AgeSignalsState.Disabled && ev.Logged };
        }, TimeSpan.FromSeconds(40));
    }

    private static IAgeSignalsProvider ProviderFor(FakeAgeSignalsManager fake, Func<Activity?> activity) =>
        new AndroidAgeSignalsProvider(new GoogleAgeSignalsBackend(activity, manager: fake));

    private static Java.Lang.Integer Int(int v) => Java.Lang.Integer.ValueOf(v)!;

    private static FakeAgeSignalsManager Make(Setup s)
    {
        var fake = new FakeAgeSignalsManager();
        fake.SetNextAgeSignalsAccessResult(AgeSignalsAccessResult.InvokeBuilder()!.SetAgeSignalsStatus(Int(s.Access))!.Build()!);
        var builder = AgeSignalsResult.InvokeBuilder()!;
        if (s.Lower is { } lo) builder.SetAgeLower(Int(lo));
        if (s.Upper is { } up) builder.SetAgeUpper(Int(up));
        if (s.Change is { } change) builder.SetSignificantChangeStatus(Int(change));
        fake.SetNextAgeSignalsResult(builder.Build()!);
        if (s.ErrorCode is { } code)
        {
            if (s.ErrorOnAccess) fake.SetNextRequestAgeSignalsAccessException(new AgeSignalsException(code));
            if (s.ErrorOnCheck) fake.SetNextAgeSignalsException(new AgeSignalsException(code));
        }
        return fake;
    }

    /// <summary>Calls a separate fake directly and returns the managed exception type names (outer to inner), joined with " &gt; ". Never the message.</summary>
    private static async Task<string> ManagedTypeNamesAsync(Setup setup)
    {
        var fake = Make(setup);
        try
        {
            if (setup.ErrorOnAccess)
            {
                var activity = Platform.CurrentActivity;
                if (activity is null) return "no-activity";
                var request = AgeSignalsAccessRequest.InvokeBuilder()!.SetActivity(activity)!.Build()!;
                await fake.RequestAgeSignalsAccess(request)!.AsAsync<AgeSignalsAccessResult>();
            }
            else
            {
                await fake.CheckAgeSignals(AgeSignalsRequest.InvokeBuilder()!.Build()!)!.AsAsync<AgeSignalsResult>();
            }
            return "no-exception";
        }
        catch (Exception ex)
        {
            var names = new List<string>();
            for (Exception? e = ex; e is not null; e = e.InnerException) names.Add(e.GetType().Name);
            return string.Join(" > ", names);
        }
    }
}
#endif
