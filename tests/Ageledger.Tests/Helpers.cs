using Ageledger;
using Ageledger.Testing;

namespace Ageledger.Tests;

internal static class Helpers
{
    public static AgeSignalsOptions On(Action<AgeSignalsOptions>? tweak = null)
    {
        var o = new AgeSignalsOptions { Enabled = true, AppVersion = "1.2.3", RuleId = "gate-v1" };
        tweak?.Invoke(o);
        return o;
    }

    public static (AndroidAgeSignalsProvider, FakeAndroidAgeSignalsBackend) Android()
    {
        var fake = new FakeAndroidAgeSignalsBackend();
        return (new AndroidAgeSignalsProvider(fake), fake);
    }

    public static (AppleAgeSignalsProvider, FakeAppleAgeRangeBackend) Apple()
    {
        var fake = new FakeAppleAgeRangeBackend();
        return (new AppleAgeSignalsProvider(fake), fake);
    }

    public static string TempFile() => Path.Combine(Path.GetTempPath(), "ageledger-tests-" + Guid.NewGuid().ToString("N"), "log.jsonl");
}
