namespace Ageledger;

/// <summary>
/// What <c>AddAgeledger</c> registers on a build with no platform age-signal source (plain net10.0, which Mac Catalyst and Windows heads also use).
/// It makes no call and shows nothing; with the service enabled it reports Unavailable / NotAvailable, which the policy treats like iOS below 26.
/// </summary>
internal sealed class UnsupportedAgeSignalsProvider : IAgeSignalsProvider
{
    public AgeSignalsPlatform Platform => AgeSignalsPlatform.None;

    public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgeSignalsOutcome { Platform = AgeSignalsPlatform.None, State = AgeSignalsState.Unavailable, Error = AgeSignalsError.NotAvailable });

    public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(null);

    public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken = default) => Task.FromResult<RegulatoryFeatures?>(null);

    public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignificantUpdateOutcome.Unsupported);
}
