namespace Ageledger;

/// <summary>Apple Declared Age Range: requestAgeRange plus the regulatory surface (iOS 26.2 and 26.4+).</summary>
public sealed class AppleAgeSignalsProvider(IAppleAgeRangeBackend backend) : IAgeSignalsProvider
{
    /// <inheritdoc/>
    public AgeSignalsPlatform Platform => AgeSignalsPlatform.iOS;

    /// <inheritdoc/>
    public async Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gates = options.AgeGates;
        if (!AgeSignalsOptions.AreValidAgeGates(gates))
            return new() { Platform = Platform, State = AgeSignalsState.Error, Error = AgeSignalsError.InvalidRequest };

        var response = await backend.RequestAgeRangeAsync(gates, cancellationToken).ConfigureAwait(false);
        switch (response.Kind)
        {
            case AppleResponseKind.DeclinedSharing:
                return new() { Platform = Platform, State = AgeSignalsState.Declined };
            case AppleResponseKind.Sharing:
                return new()
                {
                    Platform = Platform,
                    State = AgeSignalsState.Shared,
                    Bucket = new AgeRangeBucket(response.LowerBound, response.UpperBound),
                    ParentalControls = response.ParentalControls,
                };
            default:
                var (state, error) = response.Error switch
                {
                    AppleErrorKind.DeclinedOnboarding => (AgeSignalsState.Declined, AgeSignalsError.None),
                    AppleErrorKind.NotAvailable => (AgeSignalsState.Unavailable, AgeSignalsError.NotAvailable),
                    AppleErrorKind.InvalidAccount => (AgeSignalsState.Unavailable, AgeSignalsError.InvalidAccount),
                    AppleErrorKind.InvalidRequest => (AgeSignalsState.Error, AgeSignalsError.InvalidRequest),
                    AppleErrorKind.Network => (AgeSignalsState.Error, AgeSignalsError.Network),
                    AppleErrorKind.NoPresenter => (AgeSignalsState.Error, AgeSignalsError.NoPresenter),
                    _ => (AgeSignalsState.Error, AgeSignalsError.Other),
                };
                return new() { Platform = Platform, State = state, Error = error };
        }
    }

    /// <inheritdoc/>
    public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken = default) =>
        backend.IsEligibleForAgeFeaturesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken = default) =>
        backend.GetRequiredRegulatoryFeaturesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken = default) =>
        backend.ShowSignificantUpdateAcknowledgmentAsync(updateDescription, cancellationToken);
}
