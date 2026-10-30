namespace Ageledger;

/// <summary>
/// For a build with no platform age-signal source (plain net10.0, which Mac Catalyst and Windows heads also use).
/// It makes no call and shows nothing: <see cref="RequestAgeSignalsAsync"/> reports <see cref="AgeSignalsState.Unavailable"/> / <see cref="AgeSignalsError.NotAvailable"/>
/// (platform <see cref="AgeSignalsPlatform.None"/>), which the decision policy treats like iOS below 26. <c>AddAgeledger</c> registers it on such builds.
/// </summary>
public sealed class NoAgeSignalsProvider : IAgeSignalsProvider
{
    /// <summary>Always <see cref="AgeSignalsPlatform.None"/>.</summary>
    public AgeSignalsPlatform Platform => AgeSignalsPlatform.None;

    /// <inheritdoc/>
    public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AgeSignalsOutcome { Platform = AgeSignalsPlatform.None, State = AgeSignalsState.Unavailable, Error = AgeSignalsError.NotAvailable });
    }

    /// <inheritdoc/>
    public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<bool?>(null);
    }

    /// <inheritdoc/>
    public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<RegulatoryFeatures?>(null);
    }

    /// <inheritdoc/>
    public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SignificantUpdateOutcome.Unsupported);
    }
}
