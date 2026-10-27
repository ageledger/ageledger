namespace Ageledger;

/// <summary>
/// The integer codes the Swift wrapper (native/ios/Sources/AgeSignalsBridge.swift) returns, turned into the library's types.
/// Kept apart from the iOS-only backend so it compiles and is tested on net10.0. An unknown code maps to the error/other member.
/// </summary>
internal static class AppleCodeMap
{
    // Bits this library defines; a bit a future OS adds is dropped, so an enum value is always a combination of defined members.
    private const int KnownParentalControls = 1 | 2;
    private const int KnownRegulatoryFeatures = 1 | 2 | 4;

    // MASAgeRangeResult.kind: 0 sharing, 1 declinedSharing, 2 error
    internal static AppleResponseKind ResponseKind(int kind) => kind switch
    {
        0 => AppleResponseKind.Sharing,
        1 => AppleResponseKind.DeclinedSharing,
        _ => AppleResponseKind.Error,
    };

    // MASAgeRangeResult.errorKind: 0 none, 1 notAvailable, 2 invalidRequest, 3 invalidAccount, 4 declinedOnboarding, 5 network, 6 other
    internal static AppleErrorKind ErrorKind(int errorKind) => errorKind switch
    {
        0 => AppleErrorKind.None,
        1 => AppleErrorKind.NotAvailable,
        2 => AppleErrorKind.InvalidRequest,
        3 => AppleErrorKind.InvalidAccount,
        4 => AppleErrorKind.DeclinedOnboarding,
        5 => AppleErrorKind.Network,
        _ => AppleErrorKind.Other,
    };

    // MASAgeRangeResult.parentalControls bitmask: 1 communicationLimits, 2 significantAppChangeApprovalRequired (other bits are ignored)
    internal static ParentalControls ToParentalControls(int mask) => (ParentalControls)(mask & KnownParentalControls);

    // MASAgeSignalsBridge.isEligibleForAgeFeatures: -1 unknown or unsupported, 0 false, 1 true
    internal static bool? Eligible(int value) => value < 0 ? null : value == 1;

    // MASAgeSignalsBridge.requiredRegulatoryFeatures: -1 unknown or unsupported, else bitmask (1, 2, 4; other bits are ignored)
    internal static RegulatoryFeatures? ToRegulatoryFeatures(int value) => value < 0 ? null : (RegulatoryFeatures)(value & KnownRegulatoryFeatures);

    // MASAgeSignalsBridge.showSignificantUpdateAcknowledgment: 0 acknowledged, 1 unsupported, 2 failed
    internal static SignificantUpdateOutcome SignificantUpdate(int value) => value switch
    {
        0 => SignificantUpdateOutcome.Acknowledged,
        1 => SignificantUpdateOutcome.Unsupported,
        _ => SignificantUpdateOutcome.Failed,
    };
}
