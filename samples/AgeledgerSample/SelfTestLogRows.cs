#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ageledger;

namespace AgeledgerSample;

// The rows both platforms run: the decision log, Enabled=false, validation, policy and cancellation. The range comes from the platform's fake
// (the Play fake manager on Android, FakeAppleAgeRangeBackend on iOS), so no row can bring up a system sheet.
internal sealed partial class SelfTestRun
{
    private static readonly Regex TimestampShape = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$", RegexOptions.CultureInvariant);

    private static bool HasExactFields(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return false;
        var names = e.EnumerateObject().Select(p => p.Name).ToList();
        return names.Count == DecisionLogEntry.FieldNames.Count && DecisionLogEntry.FieldNames.All(names.Contains);
    }

    private static List<string> Lines(string path) =>
        File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).ToList() : [];

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private AgeSignalsService Service(IAgeSignalsProvider provider, Action<AgeSignalsOptions>? tweak = null, IDecisionLog? log = null) =>
        new(Options(tweak), provider, log);

    private async Task RunLogRowsAsync()
    {
        await RunCase("L1", "", async () =>
        {
            var log = NewLog("L1");
            foreach (var (lo, up) in new (int?, int?)[] { (13, 15), (16, 17), (18, null) }) await Eval(Range(lo, up), log);
            using var doc = JsonDocument.Parse(await log.ExportJsonAsync());
            var ok = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 3 && doc.RootElement.EnumerateArray().All(HasExactFields);
            var lines = Lines(LogFile("L1"));
            ok &= lines.Count == 3 && lines.All(l => { using var d = JsonDocument.Parse(l); return HasExactFields(d.RootElement); });
            return Obs.Check(ok, $"entries={lines.Count}");
        });

        await RunCase("L1b", "", async () =>
        {
            var log = NewLog("L1b");
            await Eval(Range(18, null), log);
            var text = File.ReadAllText(LogFile("L1b"));
            var lines = Lines(LogFile("L1b"));
            var ok = text.Contains("\"ageBucket\":\"18+\"", StringComparison.Ordinal) && !text.Contains("\\u002B", StringComparison.OrdinalIgnoreCase)
                && lines.Count == 1 && lines.All(l => { using var d = JsonDocument.Parse(l); return TimestampShape.IsMatch(d.RootElement.GetProperty("timestampUtc").GetString() ?? ""); });
            return Obs.Check(ok);
        });

        await RunCase("L2", "", async () =>
        {
            var log = NewLog("L2");
            await Eval(Range(13, 15), log);
            var before = Lines(LogFile("L2")).Count;
            await log.ClearAsync();
            var export = await log.ExportJsonAsync();
            return Obs.Check(before == 1 && export.Trim() == "[]" && !File.Exists(LogFile("L2")));
        });

        await RunCase("L2b", "", async () =>
        {
            var log = NewLog("L2b");
            await Eval(Range(13, 15), log);
            var dir = Path.GetDirectoryName(LogFile("L2b"))!;
            for (var i = 0; i < 2; i++) File.WriteAllText(Path.Combine(dir, $"age-decisions.jsonl.{Guid.NewGuid():N}.tmp"), "x");
            var unrelated = Path.Combine(dir, "unrelated.txt");
            File.WriteAllText(unrelated, "x");
            await log.ClearAsync();
            var left = Directory.EnumerateFiles(dir).Select(Path.GetFileName).ToList();
            return Obs.Check(left.Count == 1 && left[0] == "unrelated.txt", $"left={left.Count}");
        });

        await RunCase("L4", "", async () =>
        {
            // Every append flushes the whole file to disk, which takes about 150 ms on an older phone, so a thousand real appends can exceed the case timeout.
            // The cap is what this row checks: seed 1000 lines in the writer's format (UTF-8 without a byte-order mark, "\n" line ends), then do one real append.
            var path = LogFile("L4");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var start = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
            string Seed(int secondsAgo) =>
                $"{{\"timestampUtc\":\"{start.AddSeconds(-secondsAgo):yyyy-MM-dd'T'HH:mm:ss'Z'}\",\"ageBucket\":\"13-15\",\"decision\":\"AllowedWithParentalConsent\",\"appVersion\":\"{AppInfo.VersionString}\",\"ruleId\":\"SEED\"}}\n";
            File.WriteAllText(path, string.Concat(Enumerable.Range(0, 1000).Select(i => Seed(1000 - i))), new System.Text.UTF8Encoding(false));
            var oldest = start.AddSeconds(-1000);
            var log = new JsonFileDecisionLog(path);
            await log.AppendAsync(new DecisionLogEntry(start, "13-15", AgeDecision.AllowedWithParentalConsent, AppInfo.VersionString, "APPENDED"));
            var all = await log.ReadAllAsync();
            var ok = all.Count == 1000 && all.All(e => e.TimestampUtc != oldest) && all[^1].RuleId == "APPENDED" && all.Count(e => e.RuleId == "APPENDED") == 1
                && Lines(path).Count == 1000;
            return Obs.Check(ok, $"kept={all.Count}");
        }, TimeSpan.FromSeconds(60));

        await RunCase("L5", "", async () =>
        {
            var log = NewLog("L5");
            var counting = new CountingProvider(Range(13, 15));
            var ev = await Eval(counting, log, o => o.Enabled = false);
            var obs = Obs.From(ev);
            obs.Ok = counting.Calls == 0 && (await log.ReadAllAsync()).Count == 0 && !File.Exists(LogFile("L5"));
            obs.Detail = $"platformCalls={counting.Calls}";
            return obs;
        });

        await RunCase("L6", "", async () =>
        {
            var log = NewLog("L6");
            var a = Service(Range(13, 15), log: log);
            var b = Service(Range(16, 17), log: log);
            var both = await Task.WhenAll(a.EvaluateAsync(), b.EvaluateAsync());
            var lines = Lines(LogFile("L6"));
            var ok = both.All(e => e.Logged) && lines.Count == 2 && lines.All(l => { using var d = JsonDocument.Parse(l); return HasExactFields(d.RootElement); });
            var obs = Obs.From(both[0]);
            obs.Ok = ok;
            obs.Detail = $"entries={lines.Count}";
            return obs;
        });

        await RunCase("L7", "writable", async () =>
        {
            var log = NewLog("L7");
            var obs = Obs.From(await Eval(Range(13, 15), log));
            obs.Ok = (await log.ReadAllAsync()).Count == 1;
            return obs;
        });
        await RunCase("L7", "unwritable", async () =>
        {
            // An existing file used as a directory component, so the log can never be created.
            var dir = Path.Combine(LogRoot, "L7u");
            Directory.CreateDirectory(dir);
            var blocker = Path.Combine(dir, "not-a-directory");
            File.WriteAllText(blocker, "x");
            var log = new JsonFileDecisionLog(Path.Combine(blocker, "sub", "age-decisions.jsonl"));
            return Obs.From(await Eval(Range(13, 15), log));
        });

        await RunCase("L8", "invalid-gates-enabled", () =>
        {
            int[][] invalid = [[13, 13], [1], [13, 100], [13, 16, 18, 21], [18, 16]];
            var all = invalid.All(g => Throws<ArgumentException>(() => Service(Range(13, 15), o => o.AgeGates = g)));
            return Task.FromResult(all ? new Obs { ExceptionType = nameof(ArgumentException) } : Obs.Failed("not-thrown"));
        });
        await RunCase("L8", "invalid-rule-enabled", () =>
            Task.FromResult(Throws<ArgumentException>(() => Service(Range(13, 15), o => o.RuleId = "1bad")) ? new Obs { ExceptionType = nameof(ArgumentException) } : Obs.Failed("not-thrown")));
        await RunCase("L8", "invalid-version-enabled", () =>
            Task.FromResult(Throws<ArgumentException>(() => Service(Range(13, 15), o => o.AppVersion = "v1")) ? new Obs { ExceptionType = nameof(ArgumentException) } : Obs.Failed("not-thrown")));
        await RunCase("L8", "invalid-disabled", async () =>
            Obs.From(await Eval(Range(13, 15), NewLog("L8"), o => { o.Enabled = false; o.AgeGates = [13, 13]; o.RuleId = "1bad"; o.AppVersion = "v1"; })));

        await RunCase("L9", "valid", () =>
        {
            int[][] valid = [[2], [2, 4, 6], [97, 99], [18]];
            return Task.FromResult(Obs.Check(valid.All(g => !Throws<ArgumentException>(() => Service(Range(13, 15), o => o.AgeGates = g)))));
        });
        await RunCase("L9", "invalid", () =>
        {
            int[][] invalid = [[], [1], [100], [13, 14], [13, 16, 18, 21]];
            return Task.FromResult(Obs.Check(invalid.All(g => Throws<ArgumentException>(() => Service(Range(13, 15), o => o.AgeGates = g)))));
        });

        await RunCase("L10", "", async () =>
        {
            var exactLog = NewLog("L10e");
            var withheldLog = NewLog("L10w");
            var exact = await Eval(Range(13, 15), exactLog);
            var withheld = await Eval(Range(13, 15), withheldLog, o => o.AgeBucketLogging = AgeBucketLogging.Withheld);
            var e = await exactLog.ReadAllAsync();
            var w = await withheldLog.ReadAllAsync();
            var obs = Obs.From(exact);
            obs.Ok = withheld.Result.Bucket.Label == "13-15" && exact.Decision == withheld.Decision
                && e.Count == 1 && e[0].AgeBucket == "13-15" && w.Count == 1 && w[0].AgeBucket == "withheld";
            return obs;
        });

        await RunCase("L11", "", async () =>
        {
            var log = NewLog("L11");
            var ev = await Eval(Range(0, 12), log, o => o.AgeBucketLogging = AgeBucketLogging.Withheld);
            var entries = await log.ReadAllAsync();
            var obs = Obs.From(ev);
            obs.Bucket = entries.Count == 1 ? entries[0].AgeBucket : "none";   // the logged bucket, not the screen one
            obs.Ok = entries.Count == 1 && entries[0].Decision == AgeDecision.Blocked;
            return obs;
        });

        await RunCase("L12", "", () => EvalObs("L12", Range(0, 17), o => o.MinimumAge = 18));

        await RunCase("8.1b", "", async () =>
        {
            var log = NewLog("C0");
            var counting = new CountingProvider(Range(13, 15));
            var svc = Service(counting, log: log);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var sw = Stopwatch.StartNew();
            try
            {
                await svc.EvaluateAsync(cts.Token);
                return Obs.Failed("no-exception");
            }
            catch (OperationCanceledException ex)
            {
                var fast = sw.ElapsedMilliseconds < 2000;
                var obs = Obs.Check(fast && counting.Calls == 0 && (await log.ReadAllAsync()).Count == 0, $"platformCalls={counting.Calls}");
                obs.ExceptionType = ex.GetType().Name;
                return obs;
            }
        });
    }

    // L3 is driven by the host script: it runs "l3-write", ends the app, then runs "l3-read" in a new process.
    private async Task RunL3WriteAsync()
    {
        await RunCase("L3", "write", async () =>
        {
            var log = NewLog("L3");
            await Eval(Range(13, 15), log);
            await Eval(Range(18, null), log);
            return Obs.Check((await log.ReadAllAsync()).Count == 2);
        });
    }

    private async Task RunL3ReadAsync()
    {
        await RunCase("L3", "read", async () =>
        {
            var entries = await NewLog("L3").ReadAllAsync();
            return Obs.Check(entries.Count == 2 && entries[0].AgeBucket == "13-15" && entries[1].AgeBucket == "18+", $"entries={entries.Count}");
        });
    }
}
#endif
