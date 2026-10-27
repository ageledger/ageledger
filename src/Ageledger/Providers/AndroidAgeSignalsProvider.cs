namespace Ageledger;

/// <summary>Google Play Age Signals flow: requestAgeSignalsAccess, then checkAgeSignals only when the status is SHARED.</summary>
public sealed class AndroidAgeSignalsProvider(IAndroidAgeSignalsBackend backend) : IAgeSignalsProvider
{
    /// <inheritdoc/>
    public AgeSignalsPlatform Platform => AgeSignalsPlatform.Android;

    /// <inheritdoc/>
    public async Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var access = await backend.RequestAccessAsync(cancellationToken).ConfigureAwait(false);
        if (access.Error != AndroidErrorKind.None) return FromError(access.Error);

        switch (access.Status)
        {
            case AndroidAccessStatus.NotShared:
                return new() { Platform = Platform, State = AgeSignalsState.NotShared };
            case AndroidAccessStatus.VerificationRequired:
                return new() { Platform = Platform, State = AgeSignalsState.VerificationRequired };
            case AndroidAccessStatus.Shared:
                break;
            default:
                return new() { Platform = Platform, State = AgeSignalsState.Error, Error = AgeSignalsError.Other };
        }

        var check = await backend.CheckAgeSignalsAsync(cancellationToken).ConfigureAwait(false);
        if (check.Error != AndroidErrorKind.None) return FromError(check.Error);

        var bucket = new AgeRangeBucket(check.AgeLower, check.AgeUpper);
        return new()
        {
            Platform = Platform,
            // No bounds at all means nothing was actually shared.
            State = bucket.IsKnown ? AgeSignalsState.Shared : AgeSignalsState.NotShared,
            Bucket = bucket,
            SignificantChange = check.SignificantChange switch
            {
                AndroidSignificantChange.Approved => SignificantChange.Approved,
                AndroidSignificantChange.Pending => SignificantChange.Pending,
                AndroidSignificantChange.Declined => SignificantChange.Declined,
                _ => SignificantChange.None,
            },
        };
    }

    /// <inheritdoc/>
    public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(null);
    /// <inheritdoc/>
    public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken = default) => Task.FromResult<RegulatoryFeatures?>(null);
    /// <inheritdoc/>
    public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignificantUpdateOutcome.Unsupported);

    internal AgeSignalsOutcome FromError(AndroidErrorKind kind)
    {
        var (state, error) = kind switch
        {
            AndroidErrorKind.ApiNotAvailable => (AgeSignalsState.Unavailable, AgeSignalsError.NotAvailable),
            AndroidErrorKind.AppNotOwned => (AgeSignalsState.Unavailable, AgeSignalsError.AppNotOwned),
            AndroidErrorKind.CannotBindToService or AndroidErrorKind.PlayServicesNotFound or AndroidErrorKind.PlayServicesVersionOutdated
                or AndroidErrorKind.PlayStoreNotFound or AndroidErrorKind.PlayStoreVersionOutdated or AndroidErrorKind.SdkVersionOutdated
                => (AgeSignalsState.Unavailable, AgeSignalsError.PlatformComponentMissing),
            AndroidErrorKind.NetworkError => (AgeSignalsState.Error, AgeSignalsError.Network),
            AndroidErrorKind.NoActivity => (AgeSignalsState.Error, AgeSignalsError.NoPresenter),
            AndroidErrorKind.ClientTransientError => (AgeSignalsState.Error, AgeSignalsError.Transient),
            AndroidErrorKind.InternalError => (AgeSignalsState.Error, AgeSignalsError.Internal),
            _ => (AgeSignalsState.Error, AgeSignalsError.Other),
        };
        return new() { Platform = Platform, State = state, Error = error };
    }
}
