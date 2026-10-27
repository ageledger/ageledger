using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Ageledger;
using Ageledger.Testing;

namespace Ageledger.Tests;

/// <summary>Keeps the published examples and XML documentation honest: the Ageledger.Testing snippet runs as written, and no doc comment links to a member an app cannot see.
/// The link check here covers the net10.0 build; CI runs a parallel check (scripts/check-public-types.cs --crefs, which reads assembly metadata instead of reflecting) on the packed Android and iOS XML files and on Ageledger.Testing.xml.</summary>
public class PackageDocsTests
{
    // The code block shown in README.md ("Testing your own code") and in docs/testing-package-readme.md, between the fences, same text.
    private const string Snippet =
        "using Ageledger;\n" +
        "using Ageledger.Testing;\n" +
        "\n" +
        "var options = new AgeSignalsOptions { Enabled = true, AppVersion = \"1.0.0\", RuleId = \"gate-v1\" };\n" +
        "var fake = new FakeAppleAgeRangeBackend { NextResponse = new AppleAgeRangeResponse(AppleResponseKind.Sharing, 13, 15) };\n" +
        "var service = new AgeSignalsService(options, new AppleAgeSignalsProvider(fake));\n" +
        "\n" +
        "AgeSignalsEvaluation e = await service.EvaluateAsync();\n" +
        "// e.Result.State is AgeSignalsState.Shared, e.Result.Bucket.Label is \"13-15\",\n" +
        "// and with the placeholder policy e.Decision is AgeDecision.AllowedWithParentalConsent.\n";

    private static string RepoText(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ageledger.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]), Encoding.UTF8).Replace("\r\n", "\n");
    }

    [Fact]
    public void The_changelog_does_not_claim_that_nothing_has_run_on_an_iPhone()
    {
        // The iPhone has been run (README "Verified on devices"); the changelog must point at the README list instead of repeating an older status.
        var changelog = RepoText("CHANGELOG.md");
        Assert.DoesNotContain("Nothing has run on an iPhone", changelog);
        Assert.Contains("Unverified", changelog);
    }

    [Fact]
    public void Both_documents_show_the_snippet_this_test_runs()
    {
        Assert.Contains("```csharp\n" + Snippet + "```", RepoText("docs", "testing-package-readme.md"));
        Assert.Contains("```csharp\n" + Snippet + "```", RepoText("README.md"));
    }

    [Fact]
    public async Task The_snippet_gives_the_result_it_states()
    {
        var options = new AgeSignalsOptions { Enabled = true, AppVersion = "1.0.0", RuleId = "gate-v1" };
        var fake = new FakeAppleAgeRangeBackend { NextResponse = new AppleAgeRangeResponse(AppleResponseKind.Sharing, 13, 15) };
        var service = new AgeSignalsService(options, new AppleAgeSignalsProvider(fake));

        AgeSignalsEvaluation e = await service.EvaluateAsync();

        Assert.Equal(AgeSignalsState.Shared, e.Result.State);
        Assert.Equal("13-15", e.Result.Bucket.Label);
        Assert.Equal(AgeDecision.AllowedWithParentalConsent, e.Decision);
    }

    [Fact]
    public void No_doc_comment_links_to_a_member_that_is_not_public()
    {
        var assembly = typeof(AgeSignalsService).Assembly;
        var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        Assert.True(File.Exists(xmlPath), "Ageledger.xml is missing next to the test copy of the library.");
        var bad = new List<string>();
        var checkedCount = 0;
        foreach (var attr in XDocument.Load(xmlPath).Descendants().Attributes("cref"))
        {
            var id = attr.Value;
            if (id.StartsWith("!:", StringComparison.Ordinal)) { bad.Add(id + " (unresolved)"); continue; }
            if (id.Length < 3 || id[1] != ':' || id[0] == 'N' || !id.AsSpan(2).StartsWith("Ageledger.")) continue;
            var name = id[2..];
            var paren = name.IndexOf('(');
            // A method id names one overload: with a parameter list it has that many parameters, without one it has none (same rule as scripts/check-public-types.cs).
            int? paramCount = id[0] != 'M' ? null : paren >= 0 ? CountParameters(name[paren..]) : 0;
            if (paren >= 0) name = name[..paren];
            Type? type;
            string? member = null;
            if (id[0] == 'T') type = assembly.GetType(name);
            else
            {
                var dot = name.LastIndexOf('.');
                if (dot < 0) continue;
                member = name[(dot + 1)..];
                type = assembly.GetType(name[..dot]);
            }
            checkedCount++;
            if (type is null) { bad.Add(id + " (type not found)"); continue; }
            if (!(type.IsPublic || type.IsNestedPublic)) { bad.Add(id); continue; }
            if (member is null) continue;
            if (!MemberExposed(type, member, paramCount)) bad.Add(id);
        }
        Assert.True(checkedCount > 0, "No cref into Ageledger was found, so this check would prove nothing.");
        Assert.Empty(bad);
    }

    // Test-only type: a public method with one parameter next to an internal overload with two.
    public class OverloadFixture
    {
        public void Run(int a) { }
        internal void Run(int a, int b) { }
        internal void Run() { }
        public void Generic<T>(T value) { }
    }

    [Fact]
    public void A_cref_with_a_parameter_list_is_matched_by_overload()
    {
        var type = typeof(OverloadFixture);
        Assert.True(MemberExposed(type, "Run", 1));
        Assert.False(MemberExposed(type, "Run", 2));
        Assert.False(MemberExposed(type, "Run", 0));
        Assert.True(MemberExposed(type, "Generic``1", 1));
        Assert.True(MemberExposed(type, "Run", null));
        Assert.Equal(2, CountParameters("(System.Collections.Generic.Dictionary{System.String,System.Int32},System.Int32)"));
        Assert.Equal(0, CountParameters("()"));
        Assert.Equal(1, CountParameters("(System.String"));
    }

    private static int CountParameters(string list)
    {
        var end = list.LastIndexOf(')');
        var inner = end > 0 ? list[1..end] : list[1..];
        if (inner.Length == 0) return 0;
        int depth = 0, count = 1;
        foreach (var c in inner)
        {
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == ',' && depth == 0) count++;
        }
        return count;
    }

    private static bool MemberExposed(Type type, string member, int? paramCount)
    {
        var tick = member.IndexOf("``", StringComparison.Ordinal);
        if (tick >= 0) member = member[..tick];
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var found = member == "#ctor" ? type.GetConstructors(all).Cast<MemberInfo>() : type.GetMember(member, all);
        return found.Any(m => IsExposed(m) && (paramCount is null || m is not MethodBase b || b.GetParameters().Length == paramCount));
    }

    private static bool IsExposed(MemberInfo? m) => m switch
    {
        MethodBase b => b.IsPublic || b.IsFamily || b.IsFamilyOrAssembly,
        FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
        PropertyInfo p => IsExposed(p.GetMethod) || IsExposed(p.SetMethod),
        EventInfo e => IsExposed(e.AddMethod),
        Type t => t.IsPublic || t.IsNestedPublic,
        _ => false, // null (no accessor) and anything else
    };
}
