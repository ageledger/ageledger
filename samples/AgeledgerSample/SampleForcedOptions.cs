#if DEBUG
// This file is also compiled into tests/Ageledger.Tests (to check that these options are accepted), so it must reference library types only.
using Ageledger;

namespace AgeledgerSample;

/// <summary>Made-up options with the feature switched on, for the self-test row that resolves the service without using the saved settings. Debug builds only.</summary>
internal static class SampleForcedOptions
{
    internal static void Apply(AgeSignalsOptions options)
    {
        options.Enabled = true;
        options.AgeGates = [13, 16, 18];
        options.MinimumAge = 13;
        options.AppVersion = "1.0";
        options.RuleId = "TEST";
    }
}
#endif
