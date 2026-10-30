using System.Text;
using System.Text.RegularExpressions;
using AgeledgerSample;

namespace Ageledger.Tests;

/// <summary>
/// Keeps the sample app's on-device self-test and docs/device-test-plan.md saying the same thing. The sample is a MAUI project that
/// this test project cannot reference, so the test reads the row table in the sample's source as text: every row id there must be
/// in the plan's coverage table as a self-test row, and every self-test row of the coverage table must be in the code.
/// </summary>
public class SelfTestRowsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ageledger.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string RepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. parts]), Encoding.UTF8).Replace("\r\n", "\n");

    // Every self-test source file: SelfTest*.cs under samples/, outside build output.
    private static List<string> SelfTestSources() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "samples"), "SelfTest*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

    // Table lines in SelfTestRows.cs look like:  new("A1", "android", "fake manager", ...
    private static List<string> CodeRowIds() =>
        Regex.Matches(RepoFile("samples", "AgeledgerSample", "SelfTestRows.cs"), "^\\s*new\\(\"([^\"]+)\", \"(?:both|android|ios|android-physical|android-virtual)\"", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();

    private static string Plan() => RepoFile("docs", "device-test-plan.md");

    private static Dictionary<string, string> Coverage()
    {
        var plan = Plan();
        var start = plan.IndexOf("Coverage by automation", StringComparison.Ordinal);
        Assert.True(start >= 0, "The plan has no 'Coverage by automation' section.");
        var next = plan.IndexOf("\n#", start, StringComparison.Ordinal);
        var section = plan[start..(next < 0 ? plan.Length : next)];
        var map = new Dictionary<string, string>();
        foreach (var line in section.Split('\n'))
        {
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 4 || cells[1] is "Row" || cells[1].StartsWith("---", StringComparison.Ordinal)) continue;
            map[cells[1]] = cells[2];
        }
        return map;
    }

    [Fact]
    public void The_table_in_code_is_not_empty_and_has_no_duplicate_ids()
    {
        var ids = CodeRowIds();
        Assert.True(ids.Count >= 30, "The row table could not be read from SelfTestRows.cs.");
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Every_self_test_row_is_listed_in_the_plans_coverage_table_as_a_self_test_row()
    {
        var coverage = Coverage();
        foreach (var id in CodeRowIds())
        {
            Assert.True(coverage.TryGetValue(id, out var how), $"Row {id} is in the self-test but not in the coverage table of the plan.");
            Assert.StartsWith("self-test", how);
        }
    }

    [Fact]
    public void Every_self_test_row_of_the_coverage_table_is_in_the_code()
    {
        var ids = CodeRowIds().ToHashSet();
        foreach (var (id, how) in Coverage())
            if (how.StartsWith("self-test", StringComparison.Ordinal))
                Assert.True(ids.Contains(id), $"The plan says row {id} is automated by the self-test, but SelfTestRows.cs has no such row.");
    }

    [Fact]
    public void Every_self_test_row_id_also_has_a_row_in_the_plans_own_tables_or_section_8()
    {
        var plan = Plan();
        foreach (var id in CodeRowIds().Where(i => Regex.IsMatch(i, "^[AILH][0-9]")))
        {
            // Once in its own table (section 7 or 11) and once in the coverage table.
            var count = Regex.Matches(plan, "^\\| " + Regex.Escape(id) + " \\|", RegexOptions.Multiline).Count;
            Assert.True(count >= 2, $"Row {id} is not in a test table of the plan.");
        }
    }

    [Fact]
    public void Every_self_test_source_file_is_wrapped_in_if_DEBUG()
    {
        var files = SelfTestSources();
        Assert.True(files.Count >= 5, "The self-test source files were not found.");
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            Assert.True(lines[0] == "#if DEBUG" && lines[^1] == "#endif", $"{Path.GetFileName(file)} must start with '#if DEBUG' and end with '#endif'.");
        }
    }

    [Fact]
    public void Detail_strings_are_only_name_equals_integer_pairs()
    {
        var shape = new Regex(@"^[a-z][A-Za-z]*=\{[^}]+\}( [a-z][A-Za-z]*=\{[^}]+\})*$");
        var found = 0;
        foreach (var file in SelfTestSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, "Obs\\.Check\\([^;]*?, \\$\"([^\"]*)\"\\)|Detail = \\$\"([^\"]*)\""))
            {
                var detail = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                Assert.Matches(shape, detail);
                found++;
            }
        }
        Assert.True(found >= 5, "No detail strings were found; the pattern needs updating.");
    }

    [Fact]
    public void The_android_launch_guard_and_the_once_per_process_guard_are_in_the_source()
    {
        // The sample is a MAUI project this test project cannot run, so the guards are checked as text.
        var activity = RepoFile("samples", "AgeledgerSample", "Platforms", "Android", "MainActivity.cs");
        Assert.Contains("savedInstanceState is null", activity);
        Assert.Contains("LaunchedFromHistory", activity);
        Assert.Contains("RemoveExtra", activity);
        Assert.Contains("Interlocked.Exchange", RepoFile("samples", "AgeledgerSample", "SelfTest.cs"));
    }

    [Fact]
    public void The_real_play_rows_are_opt_in_and_recorded_not_asserted()
    {
        var rows = RepoFile("samples", "AgeledgerSample", "SelfTestRows.cs");
        foreach (var id in new[] { "A20", "A21" })
        {
            var line = rows.Split('\n').Single(l => l.TrimStart().StartsWith($"new(\"{id}\"", StringComparison.Ordinal));
            Assert.Contains("RealPlay", line);
            Assert.Contains("Asserted: false", line);
        }
        // The temporary log of the real-play suite must never hold a real bucket.
        var android = RepoFile("samples", "AgeledgerSample", "Platforms", "Android", "SelfTest.Android.cs");
        Assert.Contains("AgeBucketLogging.Withheld", android[android.IndexOf("RunRealPlayRowsAsync", StringComparison.Ordinal)..]);
    }

    private static int DefaultSuiteCases(string platform) => SelfTestRows.ExpectedCases("all", platform, false).Count;

    private static List<(string Row, string Case)> Pairs(string suite, string platform, bool isVirtual) =>
        SelfTestRows.ExpectedCases(suite, platform, isVirtual).Select(p => (p.Row.Id, p.Case.Name)).ToList();

    [Fact]
    public void Each_suite_expects_exactly_its_own_cases()
    {
        Assert.Equal(new[] { ("L3", "write") }, Pairs("l3-write", "android", false));
        Assert.Equal(new[] { ("L3", "read") }, Pairs("l3-read", "android", false));
        Assert.Equal(new[] { ("L3", "write") }, Pairs("l3-write", "ios", false));
        Assert.Equal(new[] { ("L3", "read") }, Pairs("l3-read", "ios", false));
        Assert.Equal(57, Pairs("all", "android", false).Count);
        Assert.Equal(20, Pairs("all", "ios", false).Count);
        Assert.Equal(new[] { ("A20", "") }, Pairs("real-play", "android", false));
        Assert.Equal(new[] { ("A21", "") }, Pairs("real-play", "android", true));
        Assert.Empty(Pairs("real-play", "ios", false));
    }

    [Fact]
    public void The_case_counts_of_the_default_suite_are_the_ones_the_docs_report()
    {
        // The self-test fills in a failed "not-run" case for every table case that did not run, and the host script compares the count it
        // reads with the results file's own expectedCases, so the table is the one source of the numbers. The docs must quote the same ones.
        var android = DefaultSuiteCases("android");
        var ios = DefaultSuiteCases("ios");
        Assert.True(android > ios && ios >= 20, "The table could not be read.");
        foreach (var doc in new[] { RepoFile("docs", "device-test-plan.md"), RepoFile("docs", "device-test-results.md") })
        {
            Assert.Contains($"{android} of {android}", doc);
            Assert.Contains($"{ios} of {ios}", doc);
        }
    }

    [Fact]
    public void The_sample_app_id_has_one_default_and_nothing_else_spells_it_out()
    {
        // The id (iOS bundle id, Android package) is set in one place; the Activity has no fixed name with the package in it and the
        // scripts and the UI-test project read the same value, so a different id needs no edits to tracked files.
        const string Default = "com.ageledger.sample";
        var csproj = RepoFile("samples", "AgeledgerSample", "AgeledgerSample.csproj");
        Assert.Contains("<ApplicationId>$(AgeledgerSampleId)</ApplicationId>", csproj);
        Assert.Contains($">{Default}</AgeledgerSampleId>", csproj);
        Assert.DoesNotContain(".MainActivity\"", RepoFile("samples", "AgeledgerSample", "Platforms", "Android", "MainActivity.cs"));
        Assert.Contains($"SAMPLE_ID=\"${{SAMPLE_ID:-{Default}}}\"", RepoFile("scripts", "lib", "sample-id.sh"));
        Assert.Contains("source \"$(dirname \"${BASH_SOURCE[0]}\")/sample-id.sh\"", RepoFile("scripts", "lib", "selftest-common.sh"));
        Assert.Contains("PACKAGE=\"$SAMPLE_ID\"", RepoFile("scripts", "lib", "selftest-common.sh"));
        Assert.DoesNotContain("MainActivity", RepoFile("scripts", "selftest-android.sh"));
        var yml = RepoFile("tests", "ios-device-uitests", "project.yml");
        Assert.DoesNotContain(Default, yml);
        Assert.Contains("${AGELEDGER_SAMPLE_ID}.uitests.host", yml);
    }
}
