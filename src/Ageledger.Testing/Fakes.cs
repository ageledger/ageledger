namespace Ageledger.Testing;

/// <summary>Mirrors the shape of Google's FakeAgeSignalsManager: set the next access result, next check result or next error.</summary>
public sealed class FakeAndroidAgeSignalsBackend : IAndroidAgeSignalsBackend
{
    private AndroidAccessOutcome _access = new(AndroidAccessStatus.Shared);
    private AndroidCheckOutcome _check = new(null, null, AndroidSignificantChange.None);

    /// <summary>How many times the access request was made.</summary>
    public int AccessCalls { get; private set; }
    /// <summary>How many times the age-signals check was made.</summary>
    public int CheckCalls { get; private set; }

    /// <summary>Sets the status the next access request returns.</summary>
    /// <param name="status">The status to return.</param>
    public void SetNextAccessResult(AndroidAccessStatus status) => _access = new(status);
    /// <summary>Makes the next access request return an error.</summary>
    /// <param name="error">The error to return.</param>
    public void SetNextAccessError(AndroidErrorKind error) => _access = new(AndroidAccessStatus.Unspecified, error);
    /// <summary>Sets the range and change status the next check returns.</summary>
    /// <param name="ageLower">Lower bound, or null.</param>
    /// <param name="ageUpper">Upper bound, or null for "and older".</param>
    /// <param name="change">The significant change status.</param>
    public void SetNextAgeSignalsResult(int? ageLower, int? ageUpper, AndroidSignificantChange change = AndroidSignificantChange.None) =>
        _check = new(ageLower, ageUpper, change);
    /// <summary>Makes the next check return an error.</summary>
    /// <param name="error">The error to return.</param>
    public void SetNextAgeSignalsError(AndroidErrorKind error) => _check = new(null, null, AndroidSignificantChange.None, error);

    /// <inheritdoc/>
    public Task<AndroidAccessOutcome> RequestAccessAsync(CancellationToken cancellationToken) { AccessCalls++; return Task.FromResult(_access); }
    /// <inheritdoc/>
    public Task<AndroidCheckOutcome> CheckAgeSignalsAsync(CancellationToken cancellationToken) { CheckCalls++; return Task.FromResult(_check); }
}

/// <summary>Fake for Apple's AgeRangeService.Response cases and the regulatory calls.</summary>
public sealed class FakeAppleAgeRangeBackend : IAppleAgeRangeBackend
{
    /// <summary>The response the next age range request returns.</summary>
    public AppleAgeRangeResponse NextResponse { get; set; } = new(AppleResponseKind.DeclinedSharing);
    /// <summary>What the eligibility call returns.</summary>
    public bool? Eligible { get; set; }
    /// <summary>What the regulatory features call returns.</summary>
    public RegulatoryFeatures? Features { get; set; }
    /// <summary>What the significant-update call returns.</summary>
    public SignificantUpdateOutcome UpdateOutcome { get; set; } = SignificantUpdateOutcome.Acknowledged;

    /// <summary>How many age range requests were made.</summary>
    public int RequestCalls { get; private set; }
    /// <summary>The age gates of the latest request.</summary>
    public IReadOnlyList<int>? LastGates { get; private set; }
    /// <summary>The description of the latest significant-update request.</summary>
    public string? LastUpdateDescription { get; private set; }

    /// <inheritdoc/>
    public Task<AppleAgeRangeResponse> RequestAgeRangeAsync(IReadOnlyList<int> ageGates, CancellationToken cancellationToken)
    {
        RequestCalls++;
        LastGates = ageGates;
        return Task.FromResult(NextResponse);
    }
    /// <inheritdoc/>
    public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken) => Task.FromResult(Eligible);
    /// <inheritdoc/>
    public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken) => Task.FromResult(Features);
    /// <inheritdoc/>
    public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken)
    {
        LastUpdateDescription = updateDescription;
        return Task.FromResult(UpdateOutcome);
    }
}
