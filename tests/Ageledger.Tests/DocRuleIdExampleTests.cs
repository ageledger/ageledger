using System.Text;
using System.Text.RegularExpressions;

namespace Ageledger.Tests;

/// <summary>Rule ids in the docs' and XML comments' examples are neutral labels: not a region and not an age group (which would leak the category the bucket setting can withhold).</summary>
public class DocRuleIdExampleTests
{
    private static readonly Regex AgeGroupWords = new("teen|adult|under|minor|child|kid", RegexOptions.IgnoreCase);
    private static readonly Regex RegionLike = new("^((US|EU|UK|CA|TX)([-_.]|[0-9]))", RegexOptions.IgnoreCase);
    private static readonly Regex StateLikePrefix = new("^[A-Z]{2}-"); // case-sensitive: "US-xx", not "gate-v1"
    private static readonly Regex Backticked = new("`([^`]+)`");

    private static readonly Regex[] ExamplePatterns =
    [
        new("\"ruleId\":\"([^\"]+)\""),                // JSON lines and exports
        new("RuleId = \"([^\"]+)\""),                  // options in code samples
        new("`RuleId` `([^`]+)`"),                     // prose naming a configured rule id
        new("for example `([^`]+)`\\)"),               // "(for example `x`)"
        new("^app .*, ([^,]+): \\d+$", RegexOptions.Multiline), // summary output lines
    ];

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ageledger.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(Root(), Path.Combine(file.Split('/'))), Encoding.UTF8).Replace("\r\n", "\n");

    private static List<string> ExtractFromDoc(string file)
    {
        var text = Read(file);
        var ids = ExamplePatterns.SelectMany(p => p.Matches(text).Select(m => m.Groups[1].Value)).ToList();
        if (file == "docs/decision-log-format.md")
        {
            foreach (var line in text.Split('\n'))
            {
                // The ruleId table row: every backticked token in the last cell (the examples column).
                if (line.StartsWith("| `ruleId` |", StringComparison.Ordinal))
                    ids.AddRange(Backticked.Matches(line.Split('|')[^2]).Select(m => m.Groups[1].Value));
                // The "Examples that pass:" list for rule ids.
                var start = line.IndexOf("Examples that pass:", StringComparison.Ordinal);
                if (start >= 0 && line.Contains("`ruleId`", StringComparison.Ordinal))
                {
                    var end = line.IndexOf("Examples that fail:", start, StringComparison.Ordinal);
                    var segment = end > start ? line[start..end] : line[start..];
                    ids.AddRange(Backticked.Matches(segment).Select(m => m.Groups[1].Value));
                }
            }
        }
        return ids.Distinct().ToList();
    }

    private static List<string> ExtractFromXmlComments(string file)
    {
        // Quoted ids after "such as" / "for example" on lines that talk about a rule (including the \" form inside a C# string).
        var quoted = new Regex("(?:such as|for example)[^\"\\n]*\\\\?\"([^\"\\\\]+)\\\\?\"(?:\\s+or\\s+\\\\?\"([^\"\\\\]+)\\\\?\")?");
        var ids = new List<string>();
        foreach (var line in Read(file).Split('\n'))
        {
            if (!line.Contains("rule", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match m in quoted.Matches(line))
                for (var g = 1; g < m.Groups.Count; g++)
                    if (m.Groups[g].Success) ids.Add(m.Groups[g].Value);
        }
        return ids.Distinct().ToList();
    }

    private static void AssertNeutral(string file, List<string> ids)
    {
        Assert.NotEmpty(ids);
        foreach (var id in ids)
        {
            Assert.False(AgeGroupWords.IsMatch(id), $"{file}: example rule id '{id}' names an age group");
            Assert.False(RegionLike.IsMatch(id) || StateLikePrefix.IsMatch(id), $"{file}: example rule id '{id}' looks like a region");
        }
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/decision-log-format.md")]
    [InlineData("docs/retention.md")]
    [InlineData("docs/summarising-the-log.md")]
    public void Example_rule_ids_in_docs_name_neither_an_age_group_nor_a_region(string file) =>
        AssertNeutral(file, ExtractFromDoc(file));

    [Theory]
    [InlineData("src/Ageledger/Model/AgeSignalsOptions.cs")]
    [InlineData("src/Ageledger/Logging/DecisionLogEntry.cs")]
    public void Example_rule_ids_in_xml_comments_name_neither_an_age_group_nor_a_region(string file) =>
        AssertNeutral(file, ExtractFromXmlComments(file));

    [Theory]
    [InlineData("gate-v1")]
    [InlineData("gate-v2")]
    [InlineData("signup-v1")]
    [InlineData("gate.v3")]
    [InlineData("unspecified")]
    public void The_checks_accept_the_neutral_examples(string id)
    {
        Assert.DoesNotMatch(AgeGroupWords, id);
        Assert.False(RegionLike.IsMatch(id) || StateLikePrefix.IsMatch(id));
    }

    [Theory]
    [InlineData("US-TX")]
    [InlineData("EU.DSA")]
    [InlineData("CA_1")]
    [InlineData("TX2")]
    [InlineData("NY-rule")]
    [InlineData("teen-gate")]
    public void The_checks_reject_region_and_age_group_ids(string id) =>
        Assert.True(AgeGroupWords.IsMatch(id) || RegionLike.IsMatch(id) || StateLikePrefix.IsMatch(id));
}
