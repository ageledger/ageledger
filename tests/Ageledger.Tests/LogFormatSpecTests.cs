using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Ageledger;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

/// <summary>
/// Keeps the C# code and docs/decision-log-format.md identical: the golden fixture and the validation patterns in the document must equal
/// what the code writes and validates with. Change the code and the document together.
/// </summary>
public class LogFormatSpecTests
{
    private static readonly DateTimeOffset ReadTime = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static string SpecText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ageledger.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "decision-log-format.md"), Encoding.UTF8).Replace("\r\n", "\n");
    }

    /// <summary>The body of the fenced block whose opening line is exactly three backticks plus <paramref name="info"/>.</summary>
    private static string Block(string info)
    {
        var m = Regex.Match(SpecText(), "^```" + Regex.Escape(info) + "\n(.*?)^```$", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(m.Success, $"The spec has no ```{info} block.");
        return m.Groups[1].Value;
    }

    [Fact]
    public async Task The_golden_JSON_Lines_block_is_exactly_what_the_file_log_writes_and_reads_back()
    {
        var golden = Block("jsonl");
        var source = TempFile();
        var copy = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, golden, new UTF8Encoding(false));
            var entries = await new JsonFileDecisionLog(source, time: new FixedTime(ReadTime)).ReadAllAsync();
            Assert.Equal(golden.TrimEnd('\n').Split('\n').Length, entries.Count);

            var log = new JsonFileDecisionLog(copy, time: new FixedTime(ReadTime));
            foreach (var e in entries) await log.AppendAsync(e);
            Assert.Equal(golden, File.ReadAllText(copy, new UTF8Encoding(false)));
            var bytes = File.ReadAllBytes(copy);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "No byte-order mark.");
            Assert.DoesNotContain((byte)'\r', bytes);
        }
        finally
        {
            foreach (var f in new[] { source, copy }) try { Directory.Delete(Path.GetDirectoryName(f)!, true); } catch { }
        }
    }

    [Fact]
    public async Task The_golden_export_array_is_exactly_what_both_logs_export()
    {
        var jsonl = Block("jsonl");
        var export = Block("json").TrimEnd('\n');
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, jsonl, new UTF8Encoding(false));
            var file = new JsonFileDecisionLog(path, time: new FixedTime(ReadTime));
            Assert.Equal(export, await file.ExportJsonAsync());

            var memory = new InMemoryDecisionLog();
            foreach (var e in await file.ReadAllAsync()) await memory.AppendAsync(e);
            Assert.Equal(export, await memory.ExportJsonAsync());
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void The_golden_block_uses_every_decision_and_both_special_buckets()
    {
        var text = Block("jsonl");
        foreach (var d in Enum.GetNames<AgeDecision>()) Assert.Contains($"\"decision\":\"{d}\"", text);
        Assert.Contains("\"ageBucket\":\"unknown\"", text);
        Assert.Contains("\"ageBucket\":\"withheld\"", text);
        Assert.Contains("\"ageBucket\":\"0-17\"", text);
        Assert.Contains("\"ageBucket\":\"18+\"", text);
    }

    [Theory]
    [InlineData("BucketPattern")]
    [InlineData("VersionPattern")]
    [InlineData("RulePattern")]
    [InlineData("LooksLikeDate")]
    [InlineData("HexRun")]
    [InlineData("TimestampPattern")]
    public void The_validation_patterns_in_the_spec_are_the_ones_the_code_uses(string field)
    {
        var info = typeof(DecisionLogEntry).GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(info is not null, $"DecisionLogEntry has no private static field {field}.");
        var regex = (Regex)info!.GetValue(null)!;
        Assert.Equal(RegexOptions.CultureInvariant, regex.Options);
        // the pattern must be a whole line inside a fenced block, not a fragment of prose
        var fenced = Regex.Matches(SpecText(), "^```\n(.*?)^```$", RegexOptions.Singleline | RegexOptions.Multiline)
            .SelectMany(m => m.Groups[1].Value.Split('\n')).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(regex.ToString(), fenced);
    }

    [Fact]
    public void The_spec_shows_the_plus_escape_literally()
    {
        Assert.Contains("\\u002B", SpecText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timestamps_are_floored_to_whole_seconds_never_rounded()
    {
        var late = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddTicks(9999999);
        var e = DecisionLogEntry.From(new AgeSignalsOutcome { Platform = AgeSignalsPlatform.iOS, State = AgeSignalsState.Declined },
            AgeDecision.NotApplicable, On(), late);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero), e.TimestampUtc);
        var memory = new InMemoryDecisionLog();
        await memory.AppendAsync(new DecisionLogEntry(late, "unknown", AgeDecision.NotApplicable, "1.0", "gate-v1"));
        Assert.Contains("\"timestampUtc\":\"2026-10-24T12:00:00Z\"", await memory.ExportJsonAsync());
    }

    [Theory]
    [InlineData("0000-01-01T00:00:00Z")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("2026-00-10T12:00:00Z")]
    [InlineData("2026-02-29T12:00:00Z")]
    [InlineData("2026-04-31T12:00:00Z")]
    [InlineData("2026-10-00T12:00:00Z")]
    [InlineData("2026-10-24T12:60:00Z")]
    public void Dates_and_times_that_are_not_valid_are_rejected(string ts) => Assert.Null(Parse(ts));

    [Fact]
    public void A_leap_day_in_a_leap_year_is_accepted() => Assert.NotNull(Parse("2028-02-29T12:00:00Z"));

    [Fact]
    public void The_spec_lists_the_five_field_names_and_the_four_decisions()
    {
        var text = SpecText();
        foreach (var f in DecisionLogEntry.FieldNames) Assert.Contains($"`{f}`", text);
        foreach (var d in Enum.GetNames<AgeDecision>()) Assert.Contains($"`{d}`", text);
        Assert.Equal(5, DecisionLogEntry.FieldNames.Count);
    }

    // ---- timestamp grammar: read side ----

    [Theory]
    [InlineData("2026-10-24T12:00:00Z")]
    [InlineData("2026-10-24T12:00:00+00:00")]
    [InlineData("2026-10-24T12:00:00.1Z")]
    [InlineData("2026-10-24T12:00:00.1234567Z")]
    [InlineData("2026-10-24T12:00:00.50Z")]
    [InlineData("2026-10-24T12:00:00.123+00:00")]
    public void Timestamps_the_reader_accepts(string ts) => Assert.NotNull(Parse(ts));

    [Theory]
    [InlineData("2026-10-24T12:00:00.Z")]
    [InlineData("2026-10-24T12:00:00.12345678Z")]
    [InlineData("2026-10-24T12:00:00z")]
    [InlineData("2026-10-24t12:00:00Z")]
    [InlineData("2026-10-24 12:00:00Z")]
    [InlineData("2026-10-24T12:00Z")]
    [InlineData("2026-10-24T12:00:00,5Z")]
    [InlineData("2026-10-24T12:00:00+0000")]
    [InlineData("2026-10-24T12:00:00+00")]
    [InlineData("2026-10-24T12:00:00 Z")]
    [InlineData(" 2026-10-24T12:00:00Z")]
    [InlineData("2026-10-24T12:00:00Z ")]
    [InlineData("2026-10-24T12:00:00Z\n")]
    [InlineData("2026-10-24T24:00:00Z")]
    [InlineData("2026-13-24T12:00:00Z")]
    [InlineData("2026-10-24T12:00:60Z")]
    [InlineData("26-10-24T12:00:00Z")]
    [InlineData("2026-10-24T12:00:00-00:00")]
    [InlineData("2026-10-24T12:00:00+01:00")]
    [InlineData("2026-10-24T12:00:00")]
    [InlineData("")]
    public void Timestamps_the_reader_rejects(string ts) => Assert.Null(Parse(ts));

    [Fact]
    public void Arabic_indic_digits_are_rejected_in_the_timestamp_and_the_bucket()
    {
        Assert.Null(Parse("٢٠٢٦-10-24T12:00:00Z"));
        Assert.Throws<ArgumentException>(() => DecisionLogEntry.ValidateAgeBucket("١٣-١٥"));
    }

    private static DateTimeOffset? Parse(string ts)
    {
        try
        {
            return DecisionLogEntry.FromFields(new Dictionary<string, string>
            {
                ["timestampUtc"] = ts, ["ageBucket"] = "13-15", ["decision"] = "Allowed", ["appVersion"] = "1.2.3", ["ruleId"] = "gate-v1",
            }).TimestampUtc;
        }
        catch (ArgumentException) { return null; }
    }

    // ---- bucket grammar ----

    [Theory]
    [InlineData("unknown")]
    [InlineData("withheld")]
    [InlineData("0-17")]
    [InlineData("13-15")]
    [InlineData("0-99")]
    [InlineData("18+")]
    [InlineData("0+")]
    [InlineData("99+")]
    [InlineData("05-10")]
    public void Buckets_the_code_accepts(string bucket) => DecisionLogEntry.ValidateAgeBucket(bucket);

    [Theory]
    [InlineData("Unknown")]
    [InlineData("UNKNOWN")]
    [InlineData("Withheld")]
    [InlineData("WITHHELD")]
    [InlineData("under-18")]
    [InlineData("under18")]
    [InlineData("7-7")]
    [InlineData("15-13")]
    [InlineData("100+")]
    [InlineData("0-100")]
    [InlineData("-5")]
    [InlineData("5-")]
    [InlineData("+18")]
    [InlineData("18 +")]
    [InlineData(" 18+")]
    [InlineData("18+\n")]
    [InlineData("withheld\n")]
    [InlineData("13–15")]
    [InlineData("1.5-3")]
    [InlineData("")]
    public void Buckets_the_code_rejects(string bucket) => Assert.Throws<ArgumentException>(() => DecisionLogEntry.ValidateAgeBucket(bucket));

    // ---- the file format as bytes ----

    [Fact]
    public async Task The_file_is_utf8_without_a_BOM_and_uses_LF_even_with_many_entries()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(ReadTime));
            for (var i = 0; i < 3; i++)
                await log.AppendAsync(new DecisionLogEntry(ReadTime.AddMinutes(-i), "withheld", AgeDecision.Allowed, "1.0", "gate-v1"));
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.Equal((byte)'\n', bytes[^1]);
            Assert.Equal(3, bytes.Count(b => b == (byte)'\n'));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public async Task The_reader_tolerates_a_BOM_blank_lines_and_CRLF_and_the_withheld_bucket()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string a = """{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"withheld","decision":"Allowed","appVersion":"1.0","ruleId":"gate-v1"}""";
            const string b = """{"timestampUtc":"2026-10-24T12:01:00Z","ageBucket":"0-17","decision":"Blocked","appVersion":"1.0","ruleId":"gate-v1"}""";
            File.WriteAllText(path, "\n  \n" + a + "\r\n\r\n" + b + "\n\n", new UTF8Encoding(true));
            Assert.Equal(["withheld", "0-17"], (await new JsonFileDecisionLog(path, time: new FixedTime(ReadTime)).ReadAllAsync()).Select(e => e.AgeBucket));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public async Task The_default_limits_are_1000_entries_and_365_days_on_write_and_on_read()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var log = new JsonFileDecisionLog(path, time: new FixedTime(ReadTime));
            Assert.Equal(1000, typeof(JsonFileDecisionLog).GetConstructors()[0].GetParameters().First(p => p.Name == "maxEntries").DefaultValue);
            // exactly 365 days old is kept, one second older is not
            File.WriteAllText(path,
                Line(ReadTime.AddDays(-365).AddSeconds(-1)) + "\n" + Line(ReadTime.AddDays(-365)) + "\n" + Line(ReadTime) + "\n", new UTF8Encoding(false));
            Assert.Equal(2, (await log.ReadAllAsync()).Count);
            await log.AppendAsync(new DecisionLogEntry(ReadTime, "unknown", AgeDecision.NotApplicable, "1.0", "gate-v1"));
            Assert.Equal(3, File.ReadAllLines(path).Length);
            File.WriteAllText(path, string.Concat(Enumerable.Range(0, 1001).Select(i => Line(ReadTime.AddSeconds(-1001 + i)) + "\n")), new UTF8Encoding(false));
            Assert.Equal(1000, (await log.ReadAllAsync()).Count);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    private static string Line(DateTimeOffset at) =>
        $$"""{"timestampUtc":"{{at.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}}","ageBucket":"13-15","decision":"Allowed","appVersion":"1.0","ruleId":"gate-v1"}""";
}
