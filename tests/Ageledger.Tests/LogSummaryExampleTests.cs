using System.Text;
using Ageledger;

namespace Ageledger.Tests;

/// <summary>Keeps docs/summarising-the-log.md in step: the page must contain the grouping lines below, and the test runs the same grouping over made-up entries.</summary>
public class LogSummaryExampleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // The whole method from the document (same text, same indentation).
    private const string DocLines =
        "static async Task<List<string>> SummariseAsync(IDecisionLog log)\n" +
        "{\n" +
        "    IReadOnlyList<DecisionLogEntry> entries = await log.ReadAllAsync();\n" +
        "    return entries\n" +
        "        .GroupBy(e => (e.AppVersion, e.AgeBucket, e.Decision, e.RuleId))\n" +
        "        .OrderBy(g => g.Key.AppVersion, StringComparer.Ordinal).ThenBy(g => g.Key.AgeBucket, StringComparer.Ordinal).ThenBy(g => g.Key.Decision.ToString(), StringComparer.Ordinal).ThenBy(g => g.Key.RuleId, StringComparer.Ordinal)\n" +
        "        .Select(g => $\"app {g.Key.AppVersion}, {g.Key.AgeBucket}, {g.Key.Decision}, {g.Key.RuleId}: {g.Count()}\")\n" +
        "        .ToList();\n" +
        "}\n";

    private static readonly string[] ExpectedLines =
    [
        "app 1.2.0, 18+, Allowed, gate-v1: 4",
        "app 1.2.0, unknown, NotApplicable, gate-v1: 1",
        "app 1.3.0, 18+, Allowed, signup-v1: 3",
        "app 1.3.0, withheld, Allowed, signup-v1: 1",
    ];

    // Keep identical to the code block in the document.
    private static async Task<List<string>> SummariseAsync(IDecisionLog log)
    {
        IReadOnlyList<DecisionLogEntry> entries = await log.ReadAllAsync();
        return entries
            .GroupBy(e => (e.AppVersion, e.AgeBucket, e.Decision, e.RuleId))
            .OrderBy(g => g.Key.AppVersion, StringComparer.Ordinal).ThenBy(g => g.Key.AgeBucket, StringComparer.Ordinal).ThenBy(g => g.Key.Decision.ToString(), StringComparer.Ordinal).ThenBy(g => g.Key.RuleId, StringComparer.Ordinal)
            .Select(g => $"app {g.Key.AppVersion}, {g.Key.AgeBucket}, {g.Key.Decision}, {g.Key.RuleId}: {g.Count()}")
            .ToList();
    }

    private static string DocText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ageledger.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "summarising-the-log.md"), Encoding.UTF8).Replace("\r\n", "\n");
    }

    [Fact]
    public void The_document_contains_the_method_and_the_output_lines_this_test_checks()
    {
        var doc = DocText();
        Assert.Contains(DocLines, doc);
        foreach (var line in ExpectedLines) Assert.Contains(line + "\n", doc);
    }

    [Fact]
    public async Task The_grouping_counts_entries_by_version_bucket_decision_and_rule()
    {
        var log = new InMemoryDecisionLog();
        for (var i = 0; i < 4; i++) await log.AppendAsync(new DecisionLogEntry(Now, "18+", AgeDecision.Allowed, "1.2.0", "gate-v1"));
        await log.AppendAsync(new DecisionLogEntry(Now, "unknown", AgeDecision.NotApplicable, "1.2.0", "gate-v1"));
        for (var i = 0; i < 3; i++) await log.AppendAsync(new DecisionLogEntry(Now, "18+", AgeDecision.Allowed, "1.3.0", "signup-v1"));
        await log.AppendAsync(new DecisionLogEntry(Now, "withheld", AgeDecision.Allowed, "1.3.0", "signup-v1"));

        Assert.Equal(ExpectedLines, await SummariseAsync(log));
    }
}
