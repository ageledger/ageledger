namespace Ageledger;

/// <summary>Turns a result into the app's decision. Placeholder defaults only: the app owner sets the real policy.</summary>
internal static class DecisionPolicy
{
    /// <summary>Applies the options' placeholder policy (or <see cref="AgeSignalsOptions.SharedRangePolicy"/>) to a result.</summary>
    /// <param name="result">The platform result.</param>
    /// <param name="options">The policy settings.</param>
    /// <returns>
    /// <see cref="AgeDecision.NotApplicable"/> for <see cref="AgeSignalsState.Disabled"/>; <see cref="AgeSignalsOptions.VerificationRequiredDecision"/> for <see cref="AgeSignalsState.VerificationRequired"/>;
    /// for <see cref="AgeSignalsState.Shared"/> with a known range, the custom policy's value or the default younger-end rule;
    /// otherwise (including a shared state with no range) <see cref="AgeSignalsOptions.NoSignalDecision"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException"><see cref="AgeSignalsOptions.SharedRangePolicy"/> returned a value that is not a defined <see cref="AgeDecision"/>. An exception thrown by that policy propagates unchanged.</exception>
    public static AgeDecision Decide(AgeSignalsOutcome result, AgeSignalsOptions options)
    {
        switch (result.State)
        {
            case AgeSignalsState.Disabled:
                return AgeDecision.NotApplicable;
            case AgeSignalsState.VerificationRequired:
                return options.VerificationRequiredDecision;
            case AgeSignalsState.Shared:
                if (!result.Bucket.IsKnown) return options.NoSignalDecision;
                if (options.SharedRangePolicy is { } custom)
                {
                    var decided = custom(result.Bucket);
                    // A custom policy is the app's own code: its exceptions propagate, and a value that is not a defined decision is a programming error.
                    if (!Enum.IsDefined(decided)) throw new InvalidOperationException("SharedRangePolicy returned a value that is not a defined AgeDecision.");
                    return decided;
                }
                return Default(result.Bucket, options);
            default:
                return options.NoSignalDecision;
        }
    }

    private static AgeDecision Default(AgeRangeBucket bucket, AgeSignalsOptions options)
    {
        // Younger end: the lower bound decides (null counts as 0), so a range that may include someone younger
        // is treated as the younger group. The upper bound is not used.
        var lower = bucket.Lower ?? 0;
        if (lower < options.MinimumAge) return AgeDecision.Blocked;
        if (lower < options.AdultAge) return AgeDecision.AllowedWithParentalConsent;
        return AgeDecision.Allowed;
    }
}
