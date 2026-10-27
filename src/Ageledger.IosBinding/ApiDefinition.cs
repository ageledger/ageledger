using Foundation;
using UIKit;

namespace Ageledger.Native;

[Internal]
delegate void MASAgeRangeResultHandler(MASAgeRangeResult result);
[Internal]
delegate void MASIntHandler(nint value);

// Bound against native/ios/Sources/AgeSignalsBridge.swift (Swift wrapper over Apple's DeclaredAgeRange).

[Internal]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
interface MASAgeRangeResult
{
    // 0 sharing, 1 declinedSharing, 2 error
    [Export("kind")] nint Kind { get; }
    [NullAllowed, Export("lowerBound")] NSNumber LowerBound { get; }
    [NullAllowed, Export("upperBound")] NSNumber UpperBound { get; }
    // bitmask: 1 communicationLimits, 2 significantAppChangeApprovalRequired
    [Export("parentalControls")] nint ParentalControls { get; }
    // 0 none, 1 notAvailable, 2 invalidRequest, 3 invalidAccount, 4 declinedOnboarding, 5 network, 6 other
    [Export("errorKind")] nint ErrorKind { get; }
}

[Internal]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
interface MASAgeSignalsBridge
{
    [Static, Export("requestAgeRangeWithGate1:gate2:gate3:viewController:completion:")]
    void RequestAgeRange(nint gate1, [NullAllowed] NSNumber gate2, [NullAllowed] NSNumber gate3, UIViewController viewController, MASAgeRangeResultHandler completion);

    // -1 unknown or unsupported, 0 false, 1 true
    [Static, Export("isEligibleForAgeFeaturesWithCompletion:")]
    void IsEligibleForAgeFeatures(MASIntHandler completion);

    // -1 unknown or unsupported, else bitmask (1 declaredAgeRangeRequired, 2 adult notification, 4 parental consent)
    [Static, Export("requiredRegulatoryFeaturesWithCompletion:")]
    void RequiredRegulatoryFeatures(MASIntHandler completion);

    // 0 acknowledged, 1 unsupported, 2 failed
    [Static, Export("showSignificantUpdateAcknowledgmentWithWindowScene:updateDescription:completion:")]
    void ShowSignificantUpdateAcknowledgment(UIWindowScene windowScene, string updateDescription, MASIntHandler completion);
}
