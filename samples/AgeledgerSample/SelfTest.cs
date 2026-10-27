#if DEBUG
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ageledger;
#if IOS
using Foundation;
#endif

namespace AgeledgerSample;

// Debug-only self-test of the library on a device. It is started by a launch argument (iOS: -ageledger-selftest <suite> -ageledger-nonce <hex>)
// or an intent extra (Android), builds its own services in code (never from the page's controls, never writing a preference), logs to a
// "selftest" subdirectory that it deletes afterwards, and writes selftest-results.json to the app data directory (atomically). The results hold
// no device name, serial, account or exception message: only row ids, library values, exception type names and timings.
// Rows are defined once, in SelfTestRows.cs. Nothing here is compiled into a Release build.

/// <summary>The suite and run nonce a self-test launch asked for.</summary>
internal sealed record SelfTestLaunch(string Suite, string Nonce)
{
    public const string SuiteName = "ageledger_selftest";
    public const string NonceName = "ageledger_nonce";
    private static readonly string[] Suites = ["all", "l3-write", "l3-read", "real-play"];
    private static readonly Regex NonceShape = new("^[0-9a-f]{8,64}$", RegexOptions.CultureInvariant);

    private static SelfTestLaunch? current;
    private static bool read;

    /// <summary>The requested self-test, or null for a normal launch. A missing or malformed nonce or an unknown suite means a normal launch.</summary>
    public static SelfTestLaunch? Current
    {
        get
        {
            if (!read) { read = true; current ??= FromProcessArguments(); }
            return current;
        }
        set { current = value; read = true; }
    }

    public static SelfTestLaunch? Create(string? suite, string? nonce) =>
        suite is not null && nonce is not null && Suites.Contains(suite) && NonceShape.IsMatch(nonce) ? new(suite, nonce) : null;

    private static SelfTestLaunch? FromProcessArguments()
    {
#if IOS
        var args = NSProcessInfo.ProcessInfo.Arguments;
        string? Value(string flag)
        {
            var i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        return Create(Value("-ageledger-selftest"), Value("-ageledger-nonce"));
#else
        return null;
#endif
    }
}

/// <summary>What the platform part of the self-test provides.</summary>
internal interface ISelfTestPlatform
{
    /// <summary>"android" or "ios".</summary>
    string Name { get; }
    /// <summary>How the log rows get a range: "fake manager" or "fake backend".</summary>
    string RangeSource { get; }
    /// <summary>A provider that reports the given range, with no real platform call.</summary>
    IAgeSignalsProvider RangeProvider(int? lower, int? upper);
    Task WaitUntilReadyAsync(CancellationToken cancellationToken);
    Task RunPlatformRowsAsync(SelfTestRun run);
    /// <summary>The opt-in "real-play" suite (Android only; a no-op elsewhere).</summary>
    Task RunRealPlayRowsAsync(SelfTestRun run);
}

internal static partial class SelfTestPlatform
{
    public static partial ISelfTestPlatform Create();
}

internal static class SelfTestHost
{
    private static int started;

    /// <summary>Runs the suite in the background; <paramref name="show"/> is called with the status text (it must marshal to the UI thread itself).</summary>
    public static void Start(SelfTestLaunch launch, Action<string> show)
    {
        if (Interlocked.Exchange(ref started, 1) == 1) return;   // once per process, whatever recreates the page
        show($"SELFTEST RUNNING {launch.Nonce}");
        _ = Task.Run(async () =>
        {
            string text;
            try { text = await new SelfTestRun(launch, SelfTestPlatform.Create()).RunAsync(); }
            catch (Exception ex) { text = $"SELFTEST ERROR {launch.Nonce} {ex.GetType().Name}"; }
            show(text);
        });
    }
}

/// <summary>What one case observed. Pass = <see cref="Ok"/> and every asserted expectation equal.</summary>
internal sealed class Obs
{
    public AgeSignalsState? State { get; set; }
    public string? Bucket { get; set; }
    public AgeDecision? Decision { get; set; }
    public bool? Logged { get; set; }
    public AgeSignalsError? Error { get; set; }
    public SignificantChange? Change { get; set; }
    public string? ExceptionType { get; set; }
    public bool Ok { get; set; } = true;
    private string detail = "";

    /// <summary>Only "name=integer" pairs separated by spaces, so nothing free-form (and nothing from a platform) can reach the results file.</summary>
    public string Detail
    {
        get => detail;
        set
        {
            if (value.Length > 0 && !DetailShape.IsMatch(value)) throw new ArgumentException("Detail must be name=integer pairs.");
            detail = value;
        }
    }

    private static readonly Regex DetailShape = new(@"^[a-z][A-Za-z]*=-?[0-9]{1,9}( [a-z][A-Za-z]*=-?[0-9]{1,9})*$", RegexOptions.CultureInvariant);

    public static Obs From(AgeSignalsEvaluation e) => new()
    {
        State = e.Result.State,
        Bucket = e.Result.Bucket.Label,
        Decision = e.Decision,
        Logged = e.Logged,
        Error = e.Result.Error,
        Change = e.Result.SignificantChange,
    };

    public static Obs Check(bool ok, string detail = "") => new() { Ok = ok, Detail = detail };
    public static Obs Failed(string exceptionType) => new() { Ok = false, ExceptionType = exceptionType };
}

internal sealed record CaseResult(string Row, string Case, string Source, Expect? Expected, Obs Observed, bool Pass, long ElapsedMs);

/// <summary>Counts the platform calls a service makes, to show that a path made none.</summary>
internal sealed class CountingProvider(IAgeSignalsProvider inner) : IAgeSignalsProvider
{
    public int Calls { get; private set; }
    public AgeSignalsPlatform Platform => inner.Platform;
    public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken cancellationToken = default) { Calls++; return inner.RequestAgeSignalsAsync(options, cancellationToken); }
    public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken = default) { Calls++; return inner.IsEligibleForAgeFeaturesAsync(cancellationToken); }
    public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken = default) { Calls++; return inner.GetRequiredRegulatoryFeaturesAsync(cancellationToken); }
    public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken = default) { Calls++; return inner.ShowSignificantUpdateAcknowledgmentAsync(updateDescription, cancellationToken); }
}

/// <summary>One self-test run: executes the rows that apply, then writes the results file.</summary>
internal sealed partial class SelfTestRun
{
    private const string ResultsName = "selftest-results.json";
    private static readonly TimeSpan Overall = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan DefaultCaseLimit = TimeSpan.FromSeconds(30);

    private readonly SelfTestLaunch launch;
    private readonly ISelfTestPlatform platform;
    private readonly List<CaseResult> results = [];
    private readonly string dataDir = FileSystem.AppDataDirectory;
    private CancellationToken deadline;
    private bool deadlineHit;

    public SelfTestRun(SelfTestLaunch launch, ISelfTestPlatform platform)
    {
        this.launch = launch;
        this.platform = platform;
    }

    public string LogRoot => Path.Combine(dataDir, "selftest");
    internal ISelfTestPlatform Platform => platform;
    public static bool IsVirtualDevice => DeviceInfo.Current.DeviceType == DeviceType.Virtual;

    public async Task<string> RunAsync()
    {
        var path = Path.Combine(dataDir, ResultsName);
        TryDelete(path);
        TryDelete(path + ".tmp");
        // The suite that reads what the previous launch wrote must not wipe it; every other suite starts clean.
        if (launch.Suite != "l3-read") TryDeleteDirectory(LogRoot);
        Directory.CreateDirectory(LogRoot);

        var total = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(Overall);
        deadline = cts.Token;
        var runFailed = false;
        var before = Snapshot();
        try
        {
            await platform.WaitUntilReadyAsync(deadline).ConfigureAwait(false);
            switch (launch.Suite)
            {
                case "all":
                    await RunLogRowsAsync().ConfigureAwait(false);
                    await platform.RunPlatformRowsAsync(this).ConfigureAwait(false);
                    await RunCase("L13", "", () => Task.FromResult(Obs.Check(Snapshot() == before))).ConfigureAwait(false);
                    break;
                case "l3-write": await RunL3WriteAsync().ConfigureAwait(false); break;
                case "l3-read": await RunL3ReadAsync().ConfigureAwait(false); break;
                case "real-play": await platform.RunRealPlayRowsAsync(this).ConfigureAwait(false); break;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { deadlineHit = true; }
        catch (Exception ex)
        {
            // Whatever went wrong, still write a results file that says so (type name only).
            runFailed = true;
            results.Add(new("run", "", "", null, Obs.Failed(ex.GetType().Name), false, total.ElapsedMilliseconds));
        }
        finally
        {
            // Only the l3-write suite leaves its log behind (for the relaunch); every other suite deletes it.
            if (launch.Suite != "l3-write") TryDeleteDirectory(LogRoot);
        }

        // Every (row, case) pair the table lists for this suite must have a result; a missing one is added as a failed "not-run" case.
        var expectedList = SelfTestRows.ExpectedCases(launch.Suite, platform.Name, IsVirtualDevice);
        var expectedCases = expectedList.Count;
        var backfilled = false;
        foreach (var (row, expect) in expectedList.Where(p => !results.Any(x => x.Row == p.Row.Id && x.Case == p.Case.Name)))
        {
            results.Add(new(row.Id, expect.Name, SourceOf(row), expect, Obs.Failed("not-run"), false, 0));
            backfilled = true;
        }
        var complete = !deadlineHit && !runFailed && !backfilled;

        var passed = results.Count(r => r.Pass);
        var failed = results.Count - passed;
        try
        {
            Write(path, complete, expectedCases, total.ElapsedMilliseconds, passed, failed);
        }
        catch (Exception ex)
        {
            return $"SELFTEST ERROR {launch.Nonce} {ex.GetType().Name}";
        }
        return $"SELFTEST DONE {launch.Nonce}\npassed={passed} failed={failed} complete={complete}";
    }

    private string SourceOf(SelfTestRow row) => row.Source == "range source" ? platform.RangeSource : row.Source;

    /// <summary>Runs one case of a row: finds its expectation in the table, bounds it in time, never lets an exception out except the overall deadline.</summary>
    public async Task RunCase(string row, string name, Func<Task<Obs>> body, TimeSpan? limit = null)
    {
        var def = SelfTestRows.Find(row);
        var expected = def?.Cases.FirstOrDefault(c => c.Name == name);
        var sw = Stopwatch.StartNew();
        Obs obs;
        try { obs = await body().WaitAsync(limit ?? DefaultCaseLimit, deadline).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            results.Add(new(row, name, def is null ? "" : SourceOf(def), expected, Obs.Failed("deadline"), false, sw.ElapsedMilliseconds));
            throw;
        }
        catch (TimeoutException) { obs = Obs.Failed(nameof(TimeoutException)); }
        catch (Exception ex) { obs = Obs.Failed(ex.GetType().Name); }
        var pass = expected is not null && obs.Ok && (!expected.Asserted || Matches(expected, obs));
        results.Add(new(row, name, def is null ? "" : SourceOf(def), expected, obs, pass, sw.ElapsedMilliseconds));
    }

    private static bool Matches(Expect e, Obs o) =>
        (e.State is null || e.State == o.State) && (e.Bucket is null || e.Bucket == o.Bucket) && (e.Decision is null || e.Decision == o.Decision)
        && (e.Logged is null || e.Logged == o.Logged) && (e.Error is null || e.Error == o.Error) && (e.Change is null || e.Change == o.Change);

    // ---- helpers shared by the rows -------------------------------------------------------------------------------------------------------

    /// <summary>Options for a self-test service: enabled, the documented defaults, made-up rule id, and a request timeout so nothing can hang.</summary>
    public static AgeSignalsOptions Options(Action<AgeSignalsOptions>? tweak = null)
    {
        var o = new AgeSignalsOptions
        {
            Enabled = true,
            AgeGates = [13, 16, 18],
            MinimumAge = 13,
            AdultAge = 18,
            AgeBucketLogging = AgeBucketLogging.Exact,
            AppVersion = AppInfo.VersionString,
            RuleId = "TEST",
            RequestTimeout = TimeSpan.FromSeconds(10),
        };
        tweak?.Invoke(o);
        return o;
    }

    public string LogFile(string name) => Path.Combine(LogRoot, name, "age-decisions.jsonl");

    public JsonFileDecisionLog NewLog(string name) => new(LogFile(name));

    public Task<AgeSignalsEvaluation> Eval(IAgeSignalsProvider provider, IDecisionLog? log, Action<AgeSignalsOptions>? tweak = null) =>
        new AgeSignalsService(Options(tweak), provider, log).EvaluateAsync();

    public async Task<Obs> EvalObs(string logName, IAgeSignalsProvider provider, Action<AgeSignalsOptions>? tweak = null) =>
        Obs.From(await Eval(provider, NewLog(logName), tweak).ConfigureAwait(false));

    public IAgeSignalsProvider Range(int? lower, int? upper) => platform.RangeProvider(lower, upper);

    // L13: the sample's saved settings and the manual log file, to show the self-test changed neither. The keys are the sample's own (MainPage.cs).
    private string Snapshot()
    {
        var parts = new List<string>();
        foreach (var key in new[] { "enabled", "evaluate-at-launch", "use-real-manager" })
        {
            try { parts.Add(key + "=" + (Preferences.ContainsKey(key) ? Preferences.Get(key, false).ToString() : "unset")); } catch (Exception) { parts.Add(key + "=err"); }
        }
        foreach (var key in new[] { "evaluate-at-launch-delay-ms", "fake-scenario" })
        {
            try { parts.Add(key + "=" + (Preferences.ContainsKey(key) ? Preferences.Get(key, "") : "unset")); } catch (Exception) { parts.Add(key + "=err"); }
        }
        var log = Path.Combine(dataDir, "age-decisions.jsonl");
        parts.Add("log=" + (File.Exists(log) ? new FileInfo(log).Length + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log))) : "none"));
        return string.Join("|", parts);
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (Exception) { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (Exception) { } }

    // ---- results file ---------------------------------------------------------------------------------------------------------------------

    private void Write(string path, bool complete, int expectedCases, long elapsedMs, int passed, int failed)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", 1);
            w.WriteString("nonce", launch.Nonce);
            w.WriteString("suite", launch.Suite);
            w.WriteString("platform", platform.Name);
            w.WriteString("osVersion", DeviceInfo.Current.VersionString);
            w.WriteBoolean("complete", complete);
            w.WriteBoolean("deadlineHit", deadlineHit);
            w.WriteNumber("elapsedMs", elapsedMs);
            w.WriteNumber("expectedCases", expectedCases);
            w.WriteNumber("passed", passed);
            w.WriteNumber("failed", failed);
            w.WriteStartArray("cases");
            foreach (var r in results)
            {
                var o = r.Observed;
                var e = r.Expected;
                w.WriteStartObject();
                w.WriteString("row", r.Row);
                w.WriteString("case", r.Case);
                w.WriteString("source", r.Source);
                w.WriteBoolean("asserted", e?.Asserted ?? true);
                Text(w, "expectedState", e?.State?.ToString());
                Text(w, "state", o.State?.ToString());
                Text(w, "expectedBucket", e?.Bucket);
                Text(w, "bucket", o.Bucket);
                Text(w, "expectedDecision", e?.Decision?.ToString());
                Text(w, "decision", o.Decision?.ToString());
                Flag(w, "expectedLogged", e?.Logged);
                Flag(w, "logged", o.Logged);
                Text(w, "expectedError", e?.Error?.ToString());
                Text(w, "error", o.Error?.ToString());
                Text(w, "expectedSignificantChange", e?.Change?.ToString());
                Text(w, "significantChange", o.Change?.ToString());
                Text(w, "exceptionType", o.ExceptionType);
                Text(w, "detail", o.Detail.Length == 0 ? null : o.Detail);
                w.WriteNumber("elapsedMs", r.ElapsedMs);
                w.WriteBoolean("pass", r.Pass);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        // Write beside the target, then rename, so the host never reads a half-written file.
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, stream.ToArray());
        File.Move(temp, path, overwrite: true);
    }

    private static void Text(Utf8JsonWriter w, string name, string? value) { if (value is null) w.WriteNull(name); else w.WriteString(name, value); }
    private static void Flag(Utf8JsonWriter w, string name, bool? value) { if (value is null) w.WriteNull(name); else w.WriteBoolean(name, value.Value); }
}
#endif
