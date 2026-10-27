namespace Ageledger;

/// <summary>Google's <c>AgeSignalsStatus</c> as the library reports it.</summary>
public enum AndroidAccessStatus
{
    /// <summary>No usable status (also what an unknown code maps to).</summary>
    Unspecified = 0,
    /// <summary>The person or guardian shared an age range.</summary>
    Shared = 1,
    /// <summary>Nothing was shared.</summary>
    NotShared = 2,
    /// <summary>Google Play needs the person to verify their age first.</summary>
    VerificationRequired = 3,
}

/// <summary>Google's <c>SignificantChangeStatus</c> as the library reports it.</summary>
public enum AndroidSignificantChange
{
    /// <summary>No status was reported.</summary>
    None = 0,
    /// <summary>The significant change was approved.</summary>
    Approved = 1,
    /// <summary>Approval is pending.</summary>
    Pending = 2,
    /// <summary>The significant change was declined.</summary>
    Declined = 3,
}

/// <summary>Google's AgeSignalsErrorCode values, grouped.</summary>
public enum AndroidErrorKind
{
    /// <summary>No error.</summary>
    None = 0,
    /// <summary>The Age Signals API is not available on this device or app.</summary>
    ApiNotAvailable = 1,
    /// <summary>The app was not installed by Google Play.</summary>
    AppNotOwned = 2,
    /// <summary>Google Play could not be bound to.</summary>
    CannotBindToService = 3,
    /// <summary>A transient client error; retrying later may succeed.</summary>
    ClientTransientError = 4,
    /// <summary>An internal error in Google Play.</summary>
    InternalError = 5,
    /// <summary>A network error.</summary>
    NetworkError = 6,
    /// <summary>Google Play services are missing.</summary>
    PlayServicesNotFound = 7,
    /// <summary>Google Play services are too old.</summary>
    PlayServicesVersionOutdated = 8,
    /// <summary>The Play Store is missing.</summary>
    PlayStoreNotFound = 9,
    /// <summary>The Play Store is too old.</summary>
    PlayStoreVersionOutdated = 10,
    /// <summary>The age-signals SDK is too old.</summary>
    SdkVersionOutdated = 11,
    /// <summary>Any other code (including a code this library does not know).</summary>
    Other = 12,
    /// <summary>Not a Google code: no usable foreground activity (none, finishing or destroyed) to start the access request from.</summary>
    NoActivity = 13,
}

/// <summary>Raw answer of requestAgeSignalsAccess. No identifiers.</summary>
public sealed record AndroidAccessOutcome(AndroidAccessStatus Status, AndroidErrorKind Error = AndroidErrorKind.None);

/// <summary>
/// Raw answer of checkAgeSignals. Google also returns installId and ageRangeSource; the backend drops them
/// before they reach this type.
/// </summary>
public sealed record AndroidCheckOutcome(int? AgeLower, int? AgeUpper, AndroidSignificantChange SignificantChange, AndroidErrorKind Error = AndroidErrorKind.None);

/// <summary>The thin layer over Google's AgeSignalsManager. The real one is in the Android build; tests use <c>Ageledger.Testing.FakeAndroidAgeSignalsBackend</c> from the Ageledger.Testing package.</summary>
public interface IAndroidAgeSignalsBackend
{
    /// <summary>Starts Google's access request (it may show Play's consent screen).</summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    Task<AndroidAccessOutcome> RequestAccessAsync(CancellationToken cancellationToken);
    /// <summary>Reads the age range after an access request returned <see cref="AndroidAccessStatus.Shared"/>.</summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    Task<AndroidCheckOutcome> CheckAgeSignalsAsync(CancellationToken cancellationToken);
}
