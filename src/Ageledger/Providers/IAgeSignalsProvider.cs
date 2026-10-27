namespace Ageledger;

/// <summary>One platform's age-signal source behind a neutral shape. Implementations never log.</summary>
public interface IAgeSignalsProvider
{
    /// <summary>The platform this provider reads.</summary>
    AgeSignalsPlatform Platform { get; }

    /// <summary>Asks the platform for the person's age range (Google: access request then check; Apple: requestAgeRange).</summary>
    Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken cancellationToken = default);

    /// <summary>Apple iOS 26.2+: whether the account is eligible for age features. Null when the platform cannot say.</summary>
    Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken = default);

    /// <summary>Apple iOS 26.4+: which regulatory features apply. Null when the platform cannot say.</summary>
    Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken = default);

    /// <summary>Apple iOS 26.4+: shows the system significant-change acknowledgment.</summary>
    Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken = default);
}
