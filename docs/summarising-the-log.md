# Summarising your log

An example for app teams, not part of the library's API. It turns the decision log into counts, for example to see whether a rule change behaved as you expected after a release.

The summary uses only the five logged fields (`timestampUtc`, `ageBucket`, `decision`, `appVersion`, `ruleId`). The log stays on the device unless the person using the app chooses to export it (for example to share it for a support case), so this code normally runs on that device. Rule ids are the app's own labels; the library maps no rule to any law. Do not send log rows to analytics, advertising or other outside services; the terms are in the README's decision-log section and in [retention.md](retention.md). If the app combines logs from many people, that is its own data processing; check the platform terms and state it in the privacy policy.

```csharp
using Ageledger;

// log is the same IDecisionLog you gave AgeSignalsService (for example a JsonFileDecisionLog).
static async Task<List<string>> SummariseAsync(IDecisionLog log)
{
    IReadOnlyList<DecisionLogEntry> entries = await log.ReadAllAsync();
    return entries
        .GroupBy(e => (e.AppVersion, e.AgeBucket, e.Decision, e.RuleId))
        .OrderBy(g => g.Key.AppVersion, StringComparer.Ordinal).ThenBy(g => g.Key.AgeBucket, StringComparer.Ordinal).ThenBy(g => g.Key.Decision.ToString(), StringComparer.Ordinal).ThenBy(g => g.Key.RuleId, StringComparer.Ordinal)
        .Select(g => $"app {g.Key.AppVersion}, {g.Key.AgeBucket}, {g.Key.Decision}, {g.Key.RuleId}: {g.Count()}")
        .ToList();
}
```

Show the returned lines in the app's own UI. Made-up output from one person's log, with one version change:

```
app 1.2.0, 18+, Allowed, gate-v1: 4
app 1.2.0, unknown, NotApplicable, gate-v1: 1
app 1.3.0, 18+, Allowed, signup-v1: 3
app 1.3.0, withheld, Allowed, signup-v1: 1
```

Versions sort as text (`1.10.0` comes before `1.9.0`). To count without the rule, drop `e.RuleId` from the key and from the output line. Only rows still inside the log's age and count limits are read, so counts cover that window, not everything the app ever did. A summary you save or show is a copy that the log's `retention`, `maxEntries` and `ClearAsync()` do not cover, so treat it with the same care as the log. A unit test (`LogSummaryExampleTests`) checks that this page contains the grouping code the test runs, over made-up entries.
