namespace Ageledger;

/// <summary>The case of Apple's <c>AgeRangeService.Response</c>.</summary>
public enum AppleResponseKind
{
    /// <summary>An age range was shared.</summary>
    Sharing = 0,
    /// <summary>The person declined to share.</summary>
    DeclinedSharing = 1,
    /// <summary>The call failed (see <see cref="AppleAgeRangeResponse.Error"/>).</summary>
    Error = 2,
}

/// <summary>Apple's <c>AgeRangeService.Error</c> cases, grouped.</summary>
public enum AppleErrorKind
{
    /// <summary>No error.</summary>
    None = 0,
    /// <summary>The API is not available on this OS version or device.</summary>
    NotAvailable = 1,
    /// <summary>Apple rejected the request (for example invalid age gates).</summary>
    InvalidRequest = 2,
    /// <summary>The Apple Account cannot be used for this request.</summary>
    InvalidAccount = 3,
    /// <summary>The person declined the onboarding.</summary>
    DeclinedOnboarding = 4,
    /// <summary>A network error.</summary>
    Network = 5,
    /// <summary>Any other error (including a code this library does not know).</summary>
    Other = 6,
    /// <summary>Not an Apple case: no view controller or window scene was available to present from.</summary>
    NoPresenter = 7,
}

/// <summary>Raw answer of AgeRangeService.requestAgeRange. The declaration (self/guardian/confirmed) is dropped by the backend.</summary>
public sealed record AppleAgeRangeResponse(
    AppleResponseKind Kind,
    int? LowerBound = null,
    int? UpperBound = null,
    ParentalControls ParentalControls = ParentalControls.None,
    AppleErrorKind Error = AppleErrorKind.None);

/// <summary>The thin layer over Apple's DeclaredAgeRange (through the Swift wrapper). Tests use <c>Ageledger.Testing.FakeAppleAgeRangeBackend</c> from the Ageledger.Testing package.</summary>
public interface IAppleAgeRangeBackend
{
    /// <summary>Asks Apple for the age range; shows Apple's sheet.</summary>
    /// <param name="ageGates">One to three ascending ages.</param>
    /// <param name="cancellationToken">Stops the wait (the sheet itself stays).</param>
    Task<AppleAgeRangeResponse> RequestAgeRangeAsync(IReadOnlyList<int> ageGates, CancellationToken cancellationToken);
    /// <summary>Whether the account is eligible for age features (iOS 26.2+); null when unknown or unsupported.</summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken);
    /// <summary>The regulatory features that apply (iOS 26.4+); null when unknown or unsupported.</summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken);
    /// <summary>Shows the system significant-update acknowledgment (iOS 26.4+).</summary>
    /// <param name="updateDescription">Text the system shows.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken);
}
