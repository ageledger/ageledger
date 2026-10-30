namespace Ageledger;

/// <summary>Which platform produced a signal.</summary>
public enum AgeSignalsPlatform
{
    /// <summary>No platform was asked: the service is disabled, or this build has no platform with age signals.</summary>
    None = 0,
    /// <summary>Google Play Age Signals.</summary>
    Android = 1,
    /// <summary>Apple Declared Age Range.</summary>
    iOS = 2,
}

/// <summary>What the platform told the app, in one neutral vocabulary.</summary>
public enum AgeSignalsState
{
    /// <summary>The feature flag is off. No platform call was made and nothing was logged.</summary>
    Disabled = 0,
    /// <summary>The platform shared an age range (see <see cref="AgeSignalsOutcome.Bucket"/>).</summary>
    Shared = 1,
    /// <summary>Apple: the person declined to share (or declined onboarding).</summary>
    Declined = 2,
    /// <summary>Google Play: the access request returned NOT_SHARED.</summary>
    NotShared = 3,
    /// <summary>Google Play: the access request returned VERIFICATION_REQUIRED.</summary>
    VerificationRequired = 4,
    /// <summary>The platform API is not available here (old OS, no Play Store, sideloaded build, no account).</summary>
    Unavailable = 5,
    /// <summary>A transient or unexpected failure (network, internal error, invalid request).</summary>
    Error = 6,
}

/// <summary>Neutral error categories, mapped from Google's AgeSignalsErrorCode and Apple's AgeRangeService.Error.</summary>
public enum AgeSignalsError
{
    /// <summary>No error.</summary>
    None = 0,
    /// <summary>API or OS feature not available.</summary>
    NotAvailable = 1,
    /// <summary>Android: the app was not installed by Google Play (typical for sideloaded or emulator builds).</summary>
    AppNotOwned = 2,
    /// <summary>Play Store, Play Services or the age-signals SDK is missing or too old.</summary>
    PlatformComponentMissing = 3,
    /// <summary>A network failure.</summary>
    Network = 4,
    /// <summary>Retrying later may succeed.</summary>
    Transient = 5,
    /// <summary>The platform rejected the request (for example invalid age gates).</summary>
    InvalidRequest = 6,
    /// <summary>Apple: the Apple Account cannot be used for this request.</summary>
    InvalidAccount = 7,
    /// <summary>An internal error reported by the platform.</summary>
    Internal = 8,
    /// <summary>Any failure that fits no other category.</summary>
    Other = 9,
    /// <summary>Apple: no view controller or window scene was available to show the system sheet from (for example very early at launch). Android: no usable foreground activity (none, finishing or destroyed) to show Google Play's consent screen from.</summary>
    NoPresenter = 10,
    /// <summary>The platform call did not finish within <see cref="AgeSignalsOptions.RequestTimeout"/>.</summary>
    Timeout = 11,
}

/// <summary>What the decision log stores in its <c>ageBucket</c> field.</summary>
public enum AgeBucketLogging
{
    /// <summary>The reported range label (for example "13-15" or "18+") is logged.</summary>
    Exact = 0,
    /// <summary>The literal "withheld" is logged when a range was received (the app chose not to record it); "unknown" when no range was received. A received range that cannot be written is "withheld" in every mode.</summary>
    Withheld = 1,
}

/// <summary>The app's decision after looking at the signal. This library only records it; the app decides what it means.</summary>
public enum AgeDecision
{
    /// <summary>No age-based decision: the service is disabled, or the default <see cref="AgeSignalsOptions.NoSignalDecision"/> when no usable signal arrived.</summary>
    NotApplicable = 0,
    /// <summary>The app allows the person in.</summary>
    Allowed = 1,
    /// <summary>The app allows the person in with a parent's or guardian's consent.</summary>
    AllowedWithParentalConsent = 2,
    /// <summary>The app does not allow the person in.</summary>
    Blocked = 3,
}

/// <summary>Whether a significant app change has been approved (Google Play only).</summary>
public enum SignificantChange
{
    /// <summary>No significant change status was reported.</summary>
    None = 0,
    /// <summary>The change was approved.</summary>
    Approved = 1,
    /// <summary>Approval is pending.</summary>
    Pending = 2,
    /// <summary>The change was declined.</summary>
    Declined = 3,
}

/// <summary>Apple's RegulatoryFeature values (iOS 26.4+).</summary>
[Flags]
public enum RegulatoryFeatures
{
    /// <summary>No regulatory feature applies.</summary>
    None = 0,
    /// <summary>A declared age range is required.</summary>
    DeclaredAgeRangeRequired = 1,
    /// <summary>A significant app change requires notifying an adult.</summary>
    SignificantAppChangeRequiresAdultNotification = 2,
    /// <summary>A significant app change requires parental consent.</summary>
    SignificantAppChangeRequiresParentalConsent = 4,
}

/// <summary>Apple's AgeRangeService.ParentalControls option set.</summary>
[Flags]
public enum ParentalControls
{
    /// <summary>No parental controls are reported.</summary>
    None = 0,
    /// <summary>Communication limits are on.</summary>
    CommunicationLimits = 1,
    /// <summary>A parent must approve significant app changes.</summary>
    SignificantAppChangeApprovalRequired = 2,
}

/// <summary>Outcome of asking the platform to show a significant-change acknowledgment.</summary>
public enum SignificantUpdateOutcome
{
    /// <summary>The system acknowledgment was shown and completed.</summary>
    Acknowledged = 0,
    /// <summary>The service is disabled, or the platform or OS version does not support it.</summary>
    Unsupported = 1,
    /// <summary>The platform call failed (no exception text is kept).</summary>
    Failed = 2,
}
