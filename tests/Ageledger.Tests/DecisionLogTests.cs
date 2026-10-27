using System.Reflection;
using System.Text.Json;
using Ageledger;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class DecisionLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static DecisionLogEntry Entry(DateTimeOffset? at = null, string bucket = "13-15") =>
        new(at ?? Now, bucket, AgeDecision.AllowedWithParentalConsent, "1.2.3", "gate-v1");

    // ---- data minimisation ----

    [Fact]
    public void The_entry_has_exactly_the_five_allowed_fields()
    {
        var props = typeof(DecisionLogEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToArray();
        Assert.Equal(["AgeBucket", "AppVersion", "Decision", "RuleId", "TimestampUtc"], props);
        Assert.Equal(5, DecisionLogEntry.FieldNames.Count);
    }

    [Theory]
    [InlineData("birthdate")]
    [InlineData("dateOfBirth")]
    [InlineData("installId")]
    [InlineData("accountId")]
    [InlineData("userId")]
    [InlineData("sourceTier")]
    [InlineData("ageRangeSource")]
    [InlineData("deviceId")]
    [InlineData("advertisingId")]
    public void Disallowed_fields_are_refused(string key)
    {
        var fields = new Dictionary<string, string>
        {
            ["timestampUtc"] = "2026-10-24T12:00:00Z", ["ageBucket"] = "13-15", ["decision"] = "Allowed",
            ["appVersion"] = "1.2.3", ["ruleId"] = "gate-v1", [key] = "x",
        };
        var ex = Assert.Throws<ArgumentException>(() => DecisionLogEntry.FromFields(fields));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Allowed_fields_alone_are_accepted()
    {
        var e = DecisionLogEntry.FromFields(new Dictionary<string, string>
        {
            ["timestampUtc"] = "2026-10-24T12:00:00Z", ["ageBucket"] = "18+", ["decision"] = "Allowed",
            ["appVersion"] = "2.0", ["ruleId"] = "signup-v1",
        });
        Assert.Equal("18+", e.AgeBucket);
        Assert.Equal(TimeSpan.Zero, e.TimestampUtc.Offset);
    }

    [Fact]
    public void A_missing_field_is_refused() =>
        Assert.Throws<ArgumentException>(() => DecisionLogEntry.FromFields(new Dictionary<string, string> { ["ageBucket"] = "13-15" }));

    [Theory]
    [InlineData("dob2010.05.01")]
    [InlineData("dob2010-05-01")]
    [InlineData("dob_2010_05_01")]
    [InlineData("cafebabe-dead-beef-abcd-0123456abcde")]
    [InlineData("deadbeef")]
    [InlineData("gate-0123456789ab")]
    [InlineData("2027-gate")]
    public void Rule_ids_that_look_like_dates_or_hex_identifiers_are_refused(string rule) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, "13-15", AgeDecision.Allowed, "1.0", rule));

    [Theory]
    [InlineData("gate-v1")]
    [InlineData("signup-v1")]
    [InlineData("gate.v3")]
    [InlineData("Face-1")]            // "Face" is only 4 hex characters
    public void Ordinary_rule_ids_are_accepted(string rule) =>
        Assert.Equal(rule, new DecisionLogEntry(Now, "13-15", AgeDecision.Allowed, "1.0", rule).RuleId);

    [Theory]
    [InlineData("7-7")]
    [InlineData("15-13")]
    [InlineData("0-0")]
    [InlineData("100+")]
    [InlineData("100-101")]
    public void Buckets_must_be_ranges_with_lower_below_upper(string bucket) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, bucket, AgeDecision.Allowed, "1.0", "gate-v1"));

    [Theory]
    [InlineData("0-12")]
    [InlineData("13-15")]
    [InlineData("18+")]
    [InlineData("unknown")]
    public void Valid_buckets_are_accepted(string bucket) =>
        Assert.Equal(bucket, new DecisionLogEntry(Now, bucket, AgeDecision.Allowed, "1.0", "gate-v1").AgeBucket);

    [Theory]
    [InlineData("1.0+a1b2c3d4e5f6a7b8")]
    [InlineData("1.0-a1b2c3d4e5f6a7b8")]
    [InlineData("1.4.2 (57)")]
    [InlineData("1.0.0-SNAPSHOT")]
    [InlineData("1.2.3.4.5")]
    public void App_versions_with_free_text_suffixes_are_refused(string v) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, "13-15", AgeDecision.Allowed, v, "gate-v1"));

    [Theory]
    [InlineData("1")]
    [InlineData("1.4.2")]
    [InlineData("1.4.2.99")]
    [InlineData("1.4.2-beta1")]
    [InlineData("2.0-rc.2")]
    public void Ordinary_app_versions_are_accepted(string v) =>
        Assert.Equal(v, new DecisionLogEntry(Now, "13-15", AgeDecision.Allowed, v, "gate-v1").AppVersion);

    [Fact]
    public void A_default_timestamp_is_refused() =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(default, "13-15", AgeDecision.Allowed, "1.0", "gate-v1"));

    [Theory]
    [InlineData("Allowed, Blocked")]
    [InlineData("3")]
    [InlineData(" Allowed")]
    [InlineData("Allowed ")]
    [InlineData("allowed")]
    [InlineData("")]
    public void FromFields_requires_an_exact_decision_name(string decision)
    {
        var fields = new Dictionary<string, string>
        {
            ["timestampUtc"] = "2026-10-24T12:00:00Z", ["ageBucket"] = "13-15", ["decision"] = decision,
            ["appVersion"] = "1.2.3", ["ruleId"] = "gate-v1",
        };
        Assert.Throws<ArgumentException>(() => DecisionLogEntry.FromFields(fields));
    }

    [Theory]
    [InlineData("""{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":3,"appVersion":"1.0","ruleId":"gate-v1"}""")]
    [InlineData("""{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":"3","appVersion":"1.0","ruleId":"gate-v1"}""")]
    [InlineData("""{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":"Allowed, Blocked","appVersion":"1.0","ruleId":"gate-v1"}""")]
    [InlineData("""{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":"allowed","appVersion":"1.0","ruleId":"gate-v1"}""")]
    [InlineData("""{"ageBucket":"13-15","decision":"Allowed","appVersion":"1.0","ruleId":"gate-v1"}""")]
    [InlineData("""{"timestampUtc":"0001-01-01T00:00:00Z","ageBucket":"13-15","decision":"Allowed","appVersion":"1.0","ruleId":"gate-v1"}""")]
    [InlineData("""{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","appVersion":"1.0","ruleId":"gate-v1"}""")]
    public async Task File_lines_with_bad_decisions_or_missing_fields_are_dropped(string line)
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, [line]);
            Assert.Empty(await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Timestamps_are_written_with_Z_and_both_forms_are_read()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry());
            using (var doc = JsonDocument.Parse(File.ReadAllLines(path)[0]))
                Assert.Equal("2026-10-24T12:00:00Z", doc.RootElement.GetProperty("timestampUtc").GetString());
            File.AppendAllLines(path, ["""{"timestampUtc":"2026-10-24T12:01:00+00:00","ageBucket":"18+","decision":"Allowed","appVersion":"1.0","ruleId":"gate-v1"}"""]);
            Assert.Equal(2, (await log.ReadAllAsync()).Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Retention_and_count_cap_apply_on_read_too()
    {
        var path = TempFile();
        try
        {
            var writer = new JsonFileDecisionLog(path, maxEntries: 100, retention: TimeSpan.FromDays(365), time: new FixedTime(Now));
            for (var i = 4; i >= 0; i--) await writer.AppendAsync(Entry(Now.AddDays(-i * 10)));   // oldest first: 40, 30, 20, 10, 0 days old

            var older = await new JsonFileDecisionLog(path, retention: TimeSpan.FromDays(25), time: new FixedTime(Now)).ReadAllAsync();
            Assert.Equal([Now.AddDays(-20), Now.AddDays(-10), Now], older.Select(e => e.TimestampUtc));

            var capped = await new JsonFileDecisionLog(path, maxEntries: 2, time: new FixedTime(Now)).ReadAllAsync();
            Assert.Equal([Now.AddDays(-10), Now], capped.Select(e => e.TimestampUtc));

            var later = await new JsonFileDecisionLog(path, retention: TimeSpan.FromDays(25), time: new FixedTime(Now.AddDays(30))).ReadAllAsync();
            Assert.Empty(later);
            Assert.Equal("[]", await new JsonFileDecisionLog(path, retention: TimeSpan.FromDays(25), time: new FixedTime(Now.AddDays(30))).ExportJsonAsync());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Two_instances_on_one_path_keep_every_concurrent_append()
    {
        var path = TempFile();
        try
        {
            var a = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            var b = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(() => (i % 2 == 0 ? a : b).AppendAsync(Entry(Now.AddSeconds(i)))));
            await Task.WhenAll(tasks);
            Assert.Equal(40, (await a.ReadAllAsync()).Count);
            Assert.Equal(40, (await b.ReadAllAsync()).Count);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("2010-05-01")]       // a birthdate
    [InlineData("1234567890")]       // a long number (account / install id)
    [InlineData("user@example.com")]
    [InlineData("")]
    [InlineData("a-very-long-identifier-that-is-more-than-forty-eight-characters-long")]
    public void Allowed_fields_cannot_carry_other_data_ruleId(string rule) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, "13-15", AgeDecision.Allowed, "1.0", rule));

    [Theory]
    [InlineData("2010-05-01")]
    [InlineData("13")]
    [InlineData("thirteen-ish")]
    [InlineData("TIER_C")]
    [InlineData("")]
    public void Bucket_must_be_a_range_label(string bucket) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, bucket, AgeDecision.Allowed, "1.0", "gate-v1"));

    [Theory]
    [InlineData("c0ffee00-0000-0000-0000-000000000000")]
    [InlineData("device-1234")]
    [InlineData("")]
    public void App_version_must_look_like_a_version(string v) =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now, "13-15", AgeDecision.Allowed, v, "gate-v1"));

    [Fact]
    public void Timestamp_must_be_utc() =>
        Assert.Throws<ArgumentException>(() => new DecisionLogEntry(Now.ToOffset(TimeSpan.FromHours(2)), "13-15", AgeDecision.Allowed, "1.0", "gate-v1"));

    [Fact]
    public void No_result_or_backend_type_exposes_identifiers()
    {
        var banned = new[] { "install", "account", "tier", "source", "device", "birth" };
        foreach (var t in new[] { typeof(AgeSignalsOutcome), typeof(AgeRangeBucket), typeof(AndroidAccessOutcome), typeof(AndroidCheckOutcome), typeof(AppleAgeRangeResponse) })
            foreach (var p in t.GetProperties())
                Assert.DoesNotContain(banned, b => p.Name.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    // ---- file log ----

    [Fact]
    public async Task File_log_round_trips_and_writes_only_the_five_keys()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry());
            var back = Assert.Single(await log.ReadAllAsync());
            Assert.Equal(Entry(), back);

            using var doc = JsonDocument.Parse(File.ReadAllLines(path)[0]);
            Assert.Equal(DecisionLogEntry.FieldNames.Order(), doc.RootElement.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal("AllowedWithParentalConsent", doc.RootElement.GetProperty("decision").GetString());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Export_is_one_json_array()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry());
            await log.AppendAsync(Entry(Now.AddMinutes(1), "18+"));
            using var doc = JsonDocument.Parse(await log.ExportJsonAsync());
            Assert.Equal(2, doc.RootElement.GetArrayLength());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Max_entries_keeps_the_newest()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, maxEntries: 2, time: new FixedTime(Now.AddHours(1)));
            for (var i = 0; i < 4; i++) await log.AppendAsync(Entry(Now.AddMinutes(i)));
            var all = await log.ReadAllAsync();
            Assert.Equal([Now.AddMinutes(2), Now.AddMinutes(3)], all.Select(e => e.TimestampUtc));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Entries_older_than_the_retention_are_dropped()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, retention: TimeSpan.FromDays(30), time: new FixedTime(Now));
            await log.AppendAsync(Entry(Now.AddDays(-45)));
            await log.AppendAsync(Entry(Now));
            Assert.Single(await log.ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task A_hand_edited_line_with_extra_or_invalid_data_is_ignored()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path,
            [
                """{"timestampUtc":"2026-10-24T12:00:00+00:00","ageBucket":"2010-05-01","decision":"Allowed","appVersion":"1.0","ruleId":"gate-v1"}""",
                """{"timestampUtc":"2026-10-24T12:00:00+00:00","ageBucket":"13-15","decision":"Allowed","appVersion":"1.0","ruleId":"gate-v1","installId":"abc"}""",
                "not json",
            ]);
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            var entries = await log.ReadAllAsync();
            // Line 1 (birthdate-shaped bucket) and line 3 (not JSON) are dropped. Line 2 still parses, but its extra key is not kept.
            Assert.Single(entries);
            await log.AppendAsync(Entry());
            Assert.DoesNotContain("installId", File.ReadAllText(path));
            Assert.DoesNotContain("2010-05-01", File.ReadAllText(path));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Clear_removes_the_file()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry());
            await log.ClearAsync();
            Assert.False(File.Exists(path));
            Assert.Empty(await log.ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    private static void Cleanup(string path) { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
