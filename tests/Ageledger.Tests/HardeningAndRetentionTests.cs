using System.Text.Json;
using Ageledger;
using Ageledger.Testing;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class HardeningAndRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static string Line(string ts = "2026-10-24T12:00:00Z", string bucket = "13-15", string version = "1.0", string extra = "") =>
        $$"""{"timestampUtc":"{{ts}}","ageBucket":"{{bucket}}","decision":"Allowed","appVersion":"{{version}}","ruleId":"gate-v1"{{extra}}}""";

    private static string NewFile()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static void Cleanup(string path) { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }

    private static DecisionLogEntry Entry(DateTimeOffset? at = null, string bucket = "13-15", string rule = "gate-v1") =>
        new(at ?? Now, bucket, AgeDecision.Allowed, "1.2.3", rule);

    private static AgeSignalsOptions Opt(AgeBucketLogging logging) => On(o => o.AgeBucketLogging = logging);

    private static (AgeSignalsService, InMemoryDecisionLog) AppleService(AgeSignalsOptions o, AppleAgeRangeResponse response)
    {
        var (provider, fake) = Apple();
        fake.NextResponse = response;
        var log = new InMemoryDecisionLog();
        return (new AgeSignalsService(o, provider, log, new FixedTime(Now)), log);
    }

    // ---- M1: a disabled service never validates ----

    [Fact]
    public void A_disabled_service_does_not_throw_for_values_it_would_reject_when_enabled()
    {
        var (provider, _) = Android();
        var o = new AgeSignalsOptions { AppVersion = "1.4.2 (57)", RuleId = "2027-gate", AgeGates = [1, 2], AgeBucketLogging = (AgeBucketLogging)7 };
        var svc = new AgeSignalsService(o, provider, new InMemoryDecisionLog());
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public void A_disabled_service_does_not_throw_for_android_style_version_names()
    {
        var (provider, _) = Android();
        foreach (var v in new[] { "1.0-debug", "1.4.2 (57)" })
            _ = new AgeSignalsService(new AgeSignalsOptions { AppVersion = v }, provider);
    }

    [Theory]
    [InlineData(-1, 18)]
    [InlineData(100, 100)]
    [InlineData(13, -1)]
    [InlineData(13, 100)]
    [InlineData(18, 13)]
    public void Minimum_and_adult_age_outside_0_to_99_or_out_of_order_throw_when_enabled(int minimum, int adult)
    {
        var (provider, _) = Android();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => { o.MinimumAge = minimum; o.AdultAge = adult; }), provider));
        _ = new AgeSignalsService(new AgeSignalsOptions { MinimumAge = minimum, AdultAge = adult }, provider);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(13, 13)]
    [InlineData(0, 99)]
    [InlineData(99, 99)]
    public void Minimum_and_adult_age_inside_the_range_are_accepted(int minimum, int adult)
    {
        var (provider, _) = Android();
        _ = new AgeSignalsService(On(o => { o.MinimumAge = minimum; o.AdultAge = adult; }), provider);
    }

    // ---- M2: gates ----

    [Theory]
    [InlineData(1, 13)]
    [InlineData(13, 14)]
    [InlineData(13, 13)]
    public void Gates_that_would_produce_a_one_number_range_throw_when_enabled(int a, int b)
    {
        var (provider, _) = Apple();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AgeGates = [a, b]), provider));
    }

    [Fact]
    public void Gates_one_13_18_throw_and_two_13_15_are_accepted()
    {
        var (provider, _) = Apple();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AgeGates = [1, 13, 18]), provider));
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AgeGates = [1]), provider));
        _ = new AgeSignalsService(On(o => o.AgeGates = [2, 13, 15]), provider);
    }

    [Theory]
    [InlineData(new[] { 1, 13, 18 })]
    [InlineData(new[] { 13, 14, 18 })]
    public async Task The_apple_provider_rejects_the_same_gates_without_calling_the_backend(int[] gates)
    {
        var (provider, fake) = Apple();
        var r = await provider.RequestAgeSignalsAsync(On(o => o.AgeGates = gates));
        Assert.Equal(AgeSignalsState.Error, r.State);
        Assert.Equal(AgeSignalsError.InvalidRequest, r.Error);
        Assert.Equal(0, fake.RequestCalls);
    }

    [Fact]
    public async Task Every_range_label_from_accepted_gates_passes_the_log_checks()
    {
        foreach (var gates in new[] { new[] { 2 }, [2, 4, 6], [13, 15], [13, 16, 18], [97, 99] })
        {
            var bounds = new List<(int? lo, int? hi)> { (null, gates[0] - 1) };
            for (var i = 0; i < gates.Length; i++) bounds.Add((gates[i], i + 1 < gates.Length ? gates[i + 1] - 1 : null));
            foreach (var (lo, hi) in bounds)
            {
                var (svc, log) = AppleService(On(o => o.AgeGates = gates), new(AppleResponseKind.Sharing, lo, hi));
                Assert.True((await svc.EvaluateAsync()).Logged);
                Assert.Single(await log.ReadAllAsync());
            }
        }
    }

    // ---- M3: non-ASCII digits ----

    [Theory]
    [InlineData("١٣-١٥")]
    [InlineData("1٣-15")]
    [InlineData("١٨+")]
    public void Non_ascii_digit_buckets_throw_ArgumentException(string bucket) =>
        Assert.Throws<ArgumentException>(() => Entry(bucket: bucket));

    [Theory]
    [InlineData("١.٢")]
    [InlineData("1.٢.3")]
    public void Non_ascii_digit_versions_throw_ArgumentException(string version) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, "13-15", AgeDecision.Allowed, version, "gate-v1"));

    [Fact]
    public async Task A_file_line_with_non_ascii_digits_is_dropped_and_the_next_append_succeeds()
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line(bucket: "١٣-١٥"), Line(version: "١.٢"), Line(bucket: "18+")]);
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            Assert.Equal("18+", Assert.Single(await log.ReadAllAsync()).AgeBucket);
            await log.AppendAsync(Entry());
            Assert.Equal(2, (await log.ReadAllAsync()).Count);
            Assert.DoesNotContain("١", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally { Cleanup(path); }
    }

    // ---- S1: timestamps need an explicit Z or +00:00 ----

    [Theory]
    [InlineData("2026-10-24T12:00:00Z")]
    [InlineData("2026-10-24T12:00:00+00:00")]
    [InlineData("2026-10-24T12:00:00.5Z")]
    public void FromFields_accepts_explicit_utc(string ts) =>
        Assert.Equal(Now.Add(ts.Contains(".5") ? TimeSpan.FromMilliseconds(500) : TimeSpan.Zero), FromFieldsAt(ts).TimestampUtc);

    [Theory]
    [InlineData("2026-10-24T12:00:00")]
    [InlineData("2026-10-24T12:00:00+02:00")]
    [InlineData("2026-10-24T12:00:00-05:00")]
    [InlineData("2026-10-24T12:00:00-00:00")]
    [InlineData("2026-10-24")]
    [InlineData("Oct 24 2026 12:00Z")]
    public void FromFields_rejects_no_offset_and_other_offsets(string ts) =>
        Assert.Throws<ArgumentException>(() => FromFieldsAt(ts));

    [Theory]
    [InlineData("2026-10-24T12:00:00")]
    [InlineData("2026-10-24T14:00:00+02:00")]
    [InlineData("2026-10-24T12:00:00-00:00")]
    public async Task File_lines_without_explicit_utc_are_dropped(string ts)
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line(ts: ts), Line(ts: "2026-10-24T12:00:00+00:00")]);
            Assert.Single(await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task A_timestamp_with_a_fraction_is_written_as_whole_seconds()
    {
        var path = NewFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            var e = Entry(Now.AddTicks(1234567));
            await log.AppendAsync(e);
            Assert.Contains("\"timestampUtc\":\"2026-10-24T12:00:00Z\"", await File.ReadAllTextAsync(path));
            Assert.Equal(Now, Assert.Single(await log.ReadAllAsync()).TimestampUtc);
        }
        finally { Cleanup(path); }
    }

    private static DecisionLogEntry FromFieldsAt(string ts) => DecisionLogEntry.FromFields(new Dictionary<string, string>
    {
        ["timestampUtc"] = ts, ["ageBucket"] = "13-15", ["decision"] = "Allowed", ["appVersion"] = "1.2.3", ["ruleId"] = "gate-v1",
    });

    // ---- S2: cancellation after the provider returns ----

    [Fact]
    public async Task A_token_cancelled_after_the_provider_returns_still_returns_the_evaluation()
    {
        var path = NewFile();
        try
        {
            using var cts = new CancellationTokenSource();
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            var svc = new AgeSignalsService(On(), new CancelAfterProvider(cts), new TokenCheckingLog(log), new FixedTime(Now));
            var e = await svc.EvaluateAsync(cts.Token);
            Assert.True(cts.IsCancellationRequested);
            Assert.Equal(AgeSignalsState.Shared, e.Result.State);
            Assert.True(e.Logged);
            Assert.Single(await log.ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    // Throws if it is handed a cancelled token, as a real file log does.
    private sealed class TokenCheckingLog(IDecisionLog inner) : IDecisionLog
    {
        public Task AppendAsync(DecisionLogEntry entry, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return inner.AppendAsync(entry, ct); }
        public Task<IReadOnlyList<DecisionLogEntry>> ReadAllAsync(CancellationToken ct = default) => inner.ReadAllAsync(ct);
        public Task<string> ExportJsonAsync(CancellationToken ct = default) => inner.ExportJsonAsync(ct);
        public Task ClearAsync(CancellationToken ct = default) => inner.ClearAsync(ct);
    }

    [Fact]
    public async Task A_log_that_throws_cancellation_is_reported_as_not_logged()
    {
        var (provider, _) = Android();
        var e = await new AgeSignalsService(On(), provider, new CancelledLog()).EvaluateAsync();
        Assert.False(e.Logged);
    }

    private sealed class CancelAfterProvider(CancellationTokenSource cts) : IAgeSignalsProvider
    {
        public AgeSignalsPlatform Platform => AgeSignalsPlatform.iOS;
        public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken ct = default)
        {
            cts.Cancel();
            return Task.FromResult(new AgeSignalsOutcome { Platform = Platform, State = AgeSignalsState.Shared, Bucket = new AgeRangeBucket(13, 15) });
        }
        public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken ct = default) => Task.FromResult<bool?>(null);
        public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken ct = default) => Task.FromResult<RegulatoryFeatures?>(null);
        public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string d, CancellationToken ct = default) => Task.FromResult(SignificantUpdateOutcome.Unsupported);
    }

    private sealed class CancelledLog : IDecisionLog
    {
        public Task AppendAsync(DecisionLogEntry entry, CancellationToken ct = default) => throw new OperationCanceledException();
        public Task<IReadOnlyList<DecisionLogEntry>> ReadAllAsync(CancellationToken ct = default) => throw new OperationCanceledException();
        public Task<string> ExportJsonAsync(CancellationToken ct = default) => throw new OperationCanceledException();
        public Task ClearAsync(CancellationToken ct = default) => throw new OperationCanceledException();
    }

    // ---- trailing newline, nulls, seconds, escaped duplicate keys ----

    [Theory]
    [InlineData("13-15\n", "1.2.3", "gate-v1")]
    [InlineData("13-15", "1.2.3\n", "gate-v1")]
    [InlineData("13-15", "1.2.3", "gate-v1\n")]
    [InlineData("18+\n", "1.2.3", "gate-v1")]
    public void A_trailing_newline_is_refused(string bucket, string version, string rule) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, bucket, AgeDecision.Allowed, version, rule));

    [Fact]
    public async Task A_file_line_with_an_escaped_newline_in_the_rule_id_is_dropped()
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line().Replace("gate-v1", "gate-v1\\n"), Line()]);
            Assert.Single(await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void FromFields_null_throws_ArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => DecisionLogEntry.FromFields(null!));

    [Fact]
    public void A_timestamp_without_seconds_is_refused() =>
        Assert.Throws<ArgumentException>(() => FromFieldsAt("2026-10-24T12:00Z"));

    [Fact]
    public async Task A_duplicate_key_written_with_a_json_escape_is_dropped()
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line(extra: ",\"age\\u0042ucket\":\"18+\""), Line()]);
            Assert.Single(await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    // ---- S4: duplicate keys ----

    [Theory]
    [InlineData(""","ageBucket":"18+" """)]
    [InlineData(""","decision":"Blocked" """)]
    [InlineData(""","installId":"a","installId":"b" """)]
    public async Task A_line_with_a_repeated_key_is_dropped(string extra)
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line(extra: extra.Trim()), Line()]);
            Assert.Single(await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    // ---- C: AgeBucketLogging ----

    [Fact]
    public void The_default_is_Exact() => Assert.Equal(AgeBucketLogging.Exact, new AgeSignalsOptions().AgeBucketLogging);

    [Fact]
    public void Enum_values_keep_their_numbers_and_new_members_are_last()
    {
        Assert.Equal(0, (int)AgeBucketLogging.Exact);
        Assert.Equal(1, (int)AgeBucketLogging.Withheld);
        Assert.Equal(AgeSignalsError.Timeout, Enum.GetValues<AgeSignalsError>().Last());
        Assert.Equal(AppleErrorKind.NoPresenter, Enum.GetValues<AppleErrorKind>().Last());
    }

    [Theory]
    [InlineData(13, 15, "13-15")]
    [InlineData(18, null, "18+")]
    [InlineData(null, null, "unknown")]
    public void From_with_Exact_writes_the_reported_label(int? lo, int? hi, string label) =>
        Assert.Equal(label, DecisionLogEntry.From(Shared(lo, hi), AgeDecision.Allowed, Opt(AgeBucketLogging.Exact), Now).AgeBucket);

    [Theory]
    [InlineData(13, 15)]
    [InlineData(18, null)]
    [InlineData(0, 12)]
    public void From_with_Withheld_writes_withheld_directly_without_the_service(int? lo, int? hi) =>
        Assert.Equal("withheld", DecisionLogEntry.From(Shared(lo, hi), AgeDecision.Allowed, Opt(AgeBucketLogging.Withheld), Now).AgeBucket);

    [Theory]
    [InlineData(AgeBucketLogging.Withheld)]
    [InlineData((AgeBucketLogging)7)]
    public void Withheld_and_undefined_values_write_unknown_when_no_range_was_received(AgeBucketLogging logging)
    {
        var none = new AgeSignalsOutcome { Platform = AgeSignalsPlatform.iOS, State = AgeSignalsState.Declined };
        Assert.Equal("unknown", DecisionLogEntry.From(none, AgeDecision.NotApplicable, Opt(logging), Now).AgeBucket);
    }

    [Fact]
    public void A_received_range_that_cannot_be_written_is_withheld_under_Withheld_too() =>
        Assert.Equal("withheld", DecisionLogEntry.From(Shared(7, 7), AgeDecision.Allowed, Opt(AgeBucketLogging.Withheld), Now).AgeBucket);

    [Fact]
    public void An_undefined_value_writes_withheld_when_a_range_was_received() =>
        Assert.Equal("withheld", DecisionLogEntry.From(Shared(13, 15), AgeDecision.Allowed, Opt((AgeBucketLogging)7), Now).AgeBucket);

    [Theory]
    [InlineData(7, 7)]
    [InlineData(15, 13)]
    [InlineData(0, 100)]
    public void Exact_writes_withheld_for_a_received_range_that_fails_the_bucket_rules(int lo, int hi) =>
        Assert.Equal("withheld", DecisionLogEntry.From(Shared(lo, hi), AgeDecision.Allowed, Opt(AgeBucketLogging.Exact), Now).AgeBucket);

    private static AgeSignalsOutcome Shared(int? lo, int? hi) =>
        new() { Platform = AgeSignalsPlatform.iOS, State = AgeSignalsState.Shared, Bucket = new AgeRangeBucket(lo, hi) };

    [Fact]
    public async Task Withheld_logs_withheld_but_the_caller_still_gets_the_real_result()
    {
        var (svc, log) = AppleService(Opt(AgeBucketLogging.Withheld), new(AppleResponseKind.Sharing, 13, 15));
        var e = await svc.EvaluateAsync();
        Assert.Equal(new AgeRangeBucket(13, 15), e.Result.Bucket);
        Assert.Equal(AgeDecision.AllowedWithParentalConsent, e.Decision);
        var entry = Assert.Single(await log.ReadAllAsync());
        Assert.Equal("withheld", entry.AgeBucket);
        Assert.Equal(AgeDecision.AllowedWithParentalConsent, entry.Decision);
    }

    [Fact]
    public void An_undefined_value_throws_at_construction_when_enabled()
    {
        var (provider, _) = Apple();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(Opt((AgeBucketLogging)7), provider));
    }

    [Fact]
    public async Task An_undefined_value_set_after_construction_is_ignored_by_the_service()
    {
        var o = Opt(AgeBucketLogging.Exact);
        var (svc, log) = AppleService(o, new(AppleResponseKind.Sharing, 13, 15));
        o.AgeBucketLogging = (AgeBucketLogging)7;
        var e = await svc.EvaluateAsync();
        Assert.True(e.Logged);
        Assert.Equal("13-15", Assert.Single(await log.ReadAllAsync()).AgeBucket);
    }

    [Fact]
    public void An_undefined_logging_value_never_writes_the_real_bucket_when_an_entry_is_built_directly()
    {
        var o = Opt(AgeBucketLogging.Exact);
        o.AgeBucketLogging = (AgeBucketLogging)7;
        var result = new AgeSignalsOutcome { Platform = AgeSignalsPlatform.iOS, State = AgeSignalsState.Shared, Bucket = new AgeRangeBucket(13, 15) };
        Assert.Equal("withheld", DecisionLogEntry.From(result, AgeDecision.Allowed, o, Now).AgeBucket);
    }

    [Fact]
    public async Task A_file_with_an_old_row_and_a_new_row_reads_both_and_exports_five_keys()
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line(bucket: "13-15"), Line(ts: "2026-10-24T12:01:00Z", bucket: "unknown")]);
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now.AddHours(1)));
            Assert.Equal(["13-15", "unknown"], (await log.ReadAllAsync()).Select(e => e.AgeBucket));
            using var doc = JsonDocument.Parse(await log.ExportJsonAsync());
            foreach (var row in doc.RootElement.EnumerateArray())
                Assert.Equal(DecisionLogEntry.FieldNames.Order(), row.EnumerateObject().Select(p => p.Name).Order());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Golden_fixture_export_string_for_other_languages()
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path,
            [
                """{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":"AllowedWithParentalConsent","appVersion":"1.2.3","ruleId":"gate-v2"}""",
                """{"timestampUtc":"2026-10-24T12:01:00Z","ageBucket":"18+","decision":"Allowed","appVersion":"1.2.3","ruleId":"gate-v2"}""",
                """{"timestampUtc":"2026-10-24T12:02:00Z","ageBucket":"unknown","decision":"Blocked","appVersion":"1.2.3","ruleId":"gate-v1"}""",
            ]);
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now.AddHours(1)));
            Assert.Equal(
                """[{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":"AllowedWithParentalConsent","appVersion":"1.2.3","ruleId":"gate-v2"},""" +
                """{"timestampUtc":"2026-10-24T12:01:00Z","ageBucket":"18+","decision":"Allowed","appVersion":"1.2.3","ruleId":"gate-v2"},""" +
                """{"timestampUtc":"2026-10-24T12:02:00Z","ageBucket":"unknown","decision":"Blocked","appVersion":"1.2.3","ruleId":"gate-v1"}]""",
                await log.ExportJsonAsync());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Plus_is_written_literally_and_both_the_literal_and_escaped_forms_are_read()
    {
        var path = NewFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry(bucket: "18+"));
            Assert.Contains("\"18+\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Contains("\"18+\"", await log.ExportJsonAsync(), StringComparison.Ordinal);
            File.AppendAllLines(path, [Line(ts: "2026-10-24T12:01:00Z", bucket: "18\\u002B")]);
            Assert.Equal(["18+", "18+"], (await log.ReadAllAsync()).Select(e => e.AgeBucket));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Switching_the_options_instance_after_construction_changes_nothing_a_new_service_uses_the_new_value()
    {
        var path = NewFile();
        try
        {
            var o = Opt(AgeBucketLogging.Exact);
            var (provider, fake) = Apple();
            fake.NextResponse = new(AppleResponseKind.Sharing, 13, 15);
            var svc = new AgeSignalsService(o, provider, new JsonFileDecisionLog(path, time: new FixedTime(Now)), new FixedTime(Now));
            await svc.EvaluateAsync();
            o.AgeBucketLogging = AgeBucketLogging.Withheld;
            await svc.EvaluateAsync();
            await new AgeSignalsService(o, provider, new JsonFileDecisionLog(path, time: new FixedTime(Now)), new FixedTime(Now)).EvaluateAsync();
            Assert.Equal(["13-15", "13-15", "withheld"], (await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync()).Select(e => e.AgeBucket));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Clear_after_mixed_rows_leaves_no_file_and_the_next_append_writes_one_unknown_row()
    {
        var path = NewFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry());
            await log.AppendAsync(Entry(bucket: "unknown"));
            await log.ClearAsync();
            Assert.False(File.Exists(path));
            await log.AppendAsync(Entry(bucket: "unknown"));
            Assert.Equal("unknown", Assert.Single(await log.ReadAllAsync()).AgeBucket);
            Assert.Single(File.ReadAllLines(path));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task An_expired_row_is_hidden_on_read_stays_on_disk_and_is_gone_after_the_next_append()
    {
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line(ts: "2026-01-01T00:00:00Z", bucket: "13-15")]);
            var log = new JsonFileDecisionLog(path, retention: TimeSpan.FromDays(30), time: new FixedTime(Now));
            Assert.Empty(await log.ReadAllAsync());
            Assert.Contains("13-15", File.ReadAllText(path));
            await log.AppendAsync(Entry(bucket: "unknown"));
            Assert.DoesNotContain("13-15", File.ReadAllText(path));
            Assert.Equal("unknown", Assert.Single(await log.ReadAllAsync()).AgeBucket);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Withheld_with_the_count_cap_keeps_the_newest()
    {
        var path = NewFile();
        try
        {
            var log = new JsonFileDecisionLog(path, maxEntries: 2, time: new FixedTime(Now.AddHours(1)));
            for (var i = 0; i < 4; i++) await log.AppendAsync(Entry(Now.AddMinutes(i), "unknown"));
            Assert.Equal([Now.AddMinutes(2), Now.AddMinutes(3)], (await log.ReadAllAsync()).Select(e => e.TimestampUtc));
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData(AgeBucketLogging.Exact)]
    [InlineData(AgeBucketLogging.Withheld)]
    public async Task Disabled_or_log_less_services_do_not_write_for_either_value(AgeBucketLogging logging)
    {
        var (provider, _) = Apple();
        var log = new InMemoryDecisionLog();
        var disabled = new AgeSignalsService(new AgeSignalsOptions { AgeBucketLogging = logging }, provider, log);
        Assert.False((await disabled.EvaluateAsync()).Logged);
        Assert.Empty(await log.ReadAllAsync());
        Assert.False((await new AgeSignalsService(Opt(logging), provider).EvaluateAsync()).Logged);
    }

    [Fact]
    public async Task With_Withheld_the_decision_can_still_imply_the_age_group()
    {
        // The decision field is not hidden by this setting: under the default policy Blocked means the lower bound is under 13.
        var (svc, log) = AppleService(Opt(AgeBucketLogging.Withheld), new(AppleResponseKind.Sharing, null, 12));
        await svc.EvaluateAsync();
        var entry = Assert.Single(await log.ReadAllAsync());
        Assert.Equal("withheld", entry.AgeBucket);
        Assert.Equal(AgeDecision.Blocked, entry.Decision);
    }

    [Fact]
    public async Task Withheld_with_a_custom_policy_logs_withheld_and_the_policy_decision()
    {
        var (svc, log) = AppleService(On(o => { o.AgeBucketLogging = AgeBucketLogging.Withheld; o.SharedRangePolicy = _ => AgeDecision.Allowed; }),
            new(AppleResponseKind.Sharing, 13, 15));
        Assert.Equal(AgeDecision.Allowed, (await svc.EvaluateAsync()).Decision);
        var entry = Assert.Single(await log.ReadAllAsync());
        Assert.Equal(("withheld", AgeDecision.Allowed), (entry.AgeBucket, entry.Decision));
    }

    [Theory]
    [InlineData(AndroidAccessStatus.VerificationRequired)]
    [InlineData(AndroidAccessStatus.NotShared)]
    [InlineData(AndroidAccessStatus.Unspecified)]
    public async Task Withheld_with_non_shared_android_states_logs_unknown(AndroidAccessStatus status)
    {
        var (provider, fake) = Android();
        fake.SetNextAccessResult(status);
        var log = new InMemoryDecisionLog();
        var e = await new AgeSignalsService(Opt(AgeBucketLogging.Withheld), provider, log).EvaluateAsync();
        Assert.Equal("unknown", Assert.Single(await log.ReadAllAsync()).AgeBucket);
        Assert.Equal(e.Decision, Assert.Single(await log.ReadAllAsync()).Decision);
    }

    [Fact]
    public async Task Withheld_with_apple_declined_and_error_states_logs_unknown()
    {
        foreach (var response in new[] { new AppleAgeRangeResponse(AppleResponseKind.DeclinedSharing), new(AppleResponseKind.Error, Error: AppleErrorKind.Network) })
        {
            var (svc, log) = AppleService(Opt(AgeBucketLogging.Withheld), response);
            await svc.EvaluateAsync();
            Assert.Equal("unknown", Assert.Single(await log.ReadAllAsync()).AgeBucket);
        }
    }

    [Fact]
    public async Task The_in_memory_log_works_with_Withheld_and_has_no_caps()
    {
        // InMemoryDecisionLog has no age or count limits (documented), unlike JsonFileDecisionLog.
        var (svc, log) = AppleService(Opt(AgeBucketLogging.Withheld), new(AppleResponseKind.Sharing, 18, null));
        for (var i = 0; i < 1500; i++) await svc.EvaluateAsync();
        var all = await log.ReadAllAsync();
        Assert.Equal(1500, all.Count);
        Assert.All(all, e => Assert.Equal("withheld", e.AgeBucket));
    }

    [Fact]
    public async Task FromFields_and_the_reader_still_accept_unknown_and_reject_under18()
    {
        Assert.Equal("unknown", DecisionLogEntry.FromFields(Fields("unknown")).AgeBucket);
        Assert.Throws<ArgumentException>(() => DecisionLogEntry.FromFields(Fields("under18")));
        var path = NewFile();
        try
        {
            File.WriteAllLines(path, [Line(bucket: "unknown"), Line(bucket: "under18")]);
            Assert.Equal("unknown", Assert.Single(await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync()).AgeBucket);
        }
        finally { Cleanup(path); }
    }

    private static Dictionary<string, string> Fields(string bucket) => new()
    {
        ["timestampUtc"] = "2026-10-24T12:00:00Z", ["ageBucket"] = bucket, ["decision"] = "Allowed", ["appVersion"] = "1.2.3", ["ruleId"] = "gate-v1",
    };

    [Fact]
    public async Task Two_services_with_different_modes_share_one_file_without_losing_rows()
    {
        var path = NewFile();
        try
        {
            var (provider, fake) = Apple();
            fake.NextResponse = new(AppleResponseKind.Sharing, 13, 15);
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            var exact = new AgeSignalsService(On(o => { o.RuleId = "gate-v2"; o.AgeBucketLogging = AgeBucketLogging.Exact; }), provider, log, new FixedTime(Now));
            var unknown = new AgeSignalsService(On(o => { o.RuleId = "gate-v1"; o.AgeBucketLogging = AgeBucketLogging.Withheld; }), provider, log, new FixedTime(Now));
            await Task.WhenAll(Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? exact : unknown).EvaluateAsync()));
            var rows = await log.ReadAllAsync();
            Assert.Equal(10, rows.Count);
            Assert.All(rows.Where(r => r.RuleId == "gate-v2"), r => Assert.Equal("13-15", r.AgeBucket));
            Assert.All(rows.Where(r => r.RuleId == "gate-v1"), r => Assert.Equal("withheld", r.AgeBucket));
            Assert.Equal(5, rows.Count(r => r.RuleId == "gate-v1"));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task A_change_to_the_callers_options_during_the_platform_call_has_no_effect()
    {
        var o = Opt(AgeBucketLogging.Exact);
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(o, new ChangingProvider(o), log, new FixedTime(Now));
        await svc.EvaluateAsync();
        Assert.Equal("13-15", Assert.Single(await log.ReadAllAsync()).AgeBucket);
    }

    private sealed class ChangingProvider(AgeSignalsOptions o) : IAgeSignalsProvider
    {
        public AgeSignalsPlatform Platform => AgeSignalsPlatform.iOS;
        public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken ct = default)
        {
            o.AgeBucketLogging = AgeBucketLogging.Withheld;
            return Task.FromResult(Shared(13, 15));
        }
        public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken ct = default) => Task.FromResult<bool?>(null);
        public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken ct = default) => Task.FromResult<RegulatoryFeatures?>(null);
        public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string d, CancellationToken ct = default) => Task.FromResult(SignificantUpdateOutcome.Unsupported);
    }

    [Theory]
    [InlineData("under18-gate")]
    [InlineData("minor")]
    public void Rule_ids_that_name_an_age_group_are_not_blocked_by_validation_today(string rule) =>
        Assert.Equal(rule, Entry(rule: rule).RuleId);

    // ---- ClearAsync removes orphan temporary files ----

    [Fact]
    public async Task Clear_also_removes_orphan_temporary_files_but_not_unrelated_files()
    {
        var path = NewFile();
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry());
            var orphan1 = $"{path}.{Guid.NewGuid():N}.tmp";
            var orphan2 = $"{path}.{Guid.NewGuid():N}.tmp";
            var other = Path.Combine(dir, "other.jsonl.tmp");
            var lookalike = path + ".backup.tmp";
            var keep = Path.Combine(dir, "notes.txt");
            File.WriteAllText(orphan1, Line(bucket: "13-15"));
            File.WriteAllText(orphan2, Line(bucket: "18+"));
            File.WriteAllText(other, "x");
            File.WriteAllText(lookalike, "x");
            File.WriteAllText(keep, "x");
            await log.ClearAsync();
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(orphan1));
            Assert.False(File.Exists(orphan2));
            Assert.True(File.Exists(other));
            Assert.True(File.Exists(lookalike));
            Assert.True(File.Exists(keep));
        }
        finally { Cleanup(path); if (File.Exists(path + ".backup.tmp")) File.Delete(path + ".backup.tmp"); }
    }

    [Fact]
    public async Task Clear_with_no_directory_does_not_throw()
    {
        var path = TempFile();
        await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ClearAsync();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
