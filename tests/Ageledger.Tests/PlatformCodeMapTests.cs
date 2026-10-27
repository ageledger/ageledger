using Ageledger;

namespace Ageledger.Tests;

// The literals are the platform constants' values: Google's from Xamarin.Google.Android.Play.Age.Signals 0.0.4 (read from the binding assembly),
// Apple's from the codes in native/ios/Sources/AgeSignalsBridge.swift. These tests pin what the library maps. They cannot see a changed binding:
// that is caught at compile time by AndroidCodeGuard (Android build, constants of the binding) and by keeping AgeSignalsBridge.swift and the
// ApiDefinition.cs comments in step with AppleCodeMap (iOS: the codes are the library's own).
public class PlatformCodeMapTests
{
    [Theory]
    [InlineData(0, AndroidAccessStatus.Unspecified)]          // AgeSignalsStatus.Unspecified
    [InlineData(1, AndroidAccessStatus.Shared)]               // AgeSignalsStatus.Shared
    [InlineData(2, AndroidAccessStatus.NotShared)]            // AgeSignalsStatus.NotShared
    [InlineData(3, AndroidAccessStatus.VerificationRequired)] // AgeSignalsStatus.VerificationRequired
    [InlineData(4, AndroidAccessStatus.Unspecified)]          // unknown code
    [InlineData(-1, AndroidAccessStatus.Unspecified)]         // unknown code
    public void Google_status_codes(int code, AndroidAccessStatus expected) =>
        Assert.Equal(expected, AndroidCodeMap.MapStatus(code));

    [Fact]
    public void Google_status_null_is_unspecified() => Assert.Equal(AndroidAccessStatus.Unspecified, AndroidCodeMap.MapStatus(null));

    [Theory]
    [InlineData(0, AndroidSignificantChange.None)]      // SignificantChangeStatus.Unspecified
    [InlineData(1, AndroidSignificantChange.Approved)]  // SignificantChangeStatus.Approved
    [InlineData(2, AndroidSignificantChange.Pending)]   // SignificantChangeStatus.Pending
    [InlineData(3, AndroidSignificantChange.Declined)]  // SignificantChangeStatus.Declined
    [InlineData(4, AndroidSignificantChange.None)]      // unknown code
    public void Google_significant_change_codes(int code, AndroidSignificantChange expected) =>
        Assert.Equal(expected, AndroidCodeMap.MapChange(code));

    [Fact]
    public void Google_significant_change_null_is_none() => Assert.Equal(AndroidSignificantChange.None, AndroidCodeMap.MapChange(null));

    [Theory]
    [InlineData(-1, AndroidErrorKind.ApiNotAvailable)]              // AgeSignalsErrorCode.ApiNotAvailable
    [InlineData(-2, AndroidErrorKind.PlayStoreNotFound)]            // AgeSignalsErrorCode.PlayStoreNotFound
    [InlineData(-3, AndroidErrorKind.NetworkError)]                 // AgeSignalsErrorCode.NetworkError
    [InlineData(-4, AndroidErrorKind.PlayServicesNotFound)]         // AgeSignalsErrorCode.PlayServicesNotFound
    [InlineData(-5, AndroidErrorKind.CannotBindToService)]          // AgeSignalsErrorCode.CannotBindToService
    [InlineData(-6, AndroidErrorKind.PlayStoreVersionOutdated)]     // AgeSignalsErrorCode.PlayStoreVersionOutdated
    [InlineData(-7, AndroidErrorKind.PlayServicesVersionOutdated)]  // AgeSignalsErrorCode.PlayServicesVersionOutdated
    [InlineData(-8, AndroidErrorKind.ClientTransientError)]         // AgeSignalsErrorCode.ClientTransientError
    [InlineData(-9, AndroidErrorKind.AppNotOwned)]                  // AgeSignalsErrorCode.AppNotOwned
    [InlineData(-10, AndroidErrorKind.SdkVersionOutdated)]          // AgeSignalsErrorCode.SdkVersionOutdated
    [InlineData(-100, AndroidErrorKind.InternalError)]              // AgeSignalsErrorCode.InternalError
    [InlineData(0, AndroidErrorKind.Other)]                         // AgeSignalsErrorCode.NoError is not an error here
    [InlineData(-11, AndroidErrorKind.Other)]                       // unknown code
    [InlineData(7, AndroidErrorKind.Other)]                         // unknown code
    public void Google_error_codes(int code, AndroidErrorKind expected) =>
        Assert.Equal(expected, AndroidCodeMap.MapErrorCode(code));

    [Theory]
    [InlineData(0, AppleResponseKind.Sharing)]          // kind 0: sharing
    [InlineData(1, AppleResponseKind.DeclinedSharing)]  // kind 1: declinedSharing
    [InlineData(2, AppleResponseKind.Error)]            // kind 2: error
    [InlineData(3, AppleResponseKind.Error)]            // unknown code
    [InlineData(-1, AppleResponseKind.Error)]           // unknown code
    public void Apple_response_kinds(int code, AppleResponseKind expected) =>
        Assert.Equal(expected, AppleCodeMap.ResponseKind(code));

    [Theory]
    [InlineData(0, AppleErrorKind.None)]                // errorKind 0: none
    [InlineData(1, AppleErrorKind.NotAvailable)]        // errorKind 1: notAvailable
    [InlineData(2, AppleErrorKind.InvalidRequest)]      // errorKind 2: invalidRequest
    [InlineData(3, AppleErrorKind.InvalidAccount)]      // errorKind 3: invalidAccount
    [InlineData(4, AppleErrorKind.DeclinedOnboarding)]  // errorKind 4: declinedOnboarding
    [InlineData(5, AppleErrorKind.Network)]             // errorKind 5: network
    [InlineData(6, AppleErrorKind.Other)]               // errorKind 6: other
    [InlineData(7, AppleErrorKind.Other)]               // unknown code
    [InlineData(-1, AppleErrorKind.Other)]              // unknown code
    public void Apple_error_kinds(int code, AppleErrorKind expected) =>
        Assert.Equal(expected, AppleCodeMap.ErrorKind(code));

    [Theory]
    [InlineData(0, ParentalControls.None)]
    [InlineData(1, ParentalControls.CommunicationLimits)]                                           // bit 1: communicationLimits
    [InlineData(2, ParentalControls.SignificantAppChangeApprovalRequired)]                          // bit 2: significantAppChangeApprovalRequired
    [InlineData(3, ParentalControls.CommunicationLimits | ParentalControls.SignificantAppChangeApprovalRequired)]
    [InlineData(8, ParentalControls.None)]                                                          // unknown bit 8 is dropped
    [InlineData(9, ParentalControls.CommunicationLimits)]                                           // known bit kept, unknown bit 8 dropped
    public void Apple_parental_controls_mask(int mask, ParentalControls expected) =>
        Assert.Equal(expected, AppleCodeMap.ToParentalControls(mask));

    [Theory]
    [InlineData(-1, null)]   // -1: unknown or unsupported
    [InlineData(0, false)]   // 0: false
    [InlineData(1, true)]    // 1: true
    [InlineData(2, false)]   // anything else that is not 1
    public void Apple_eligibility_codes(int code, bool? expected) =>
        Assert.Equal(expected, AppleCodeMap.Eligible(code));

    [Fact]
    public void Apple_regulatory_features_codes()
    {
        Assert.Null(AppleCodeMap.ToRegulatoryFeatures(-1));   // -1: unknown or unsupported
        Assert.Equal(RegulatoryFeatures.None, AppleCodeMap.ToRegulatoryFeatures(0));
        Assert.Equal(RegulatoryFeatures.DeclaredAgeRangeRequired, AppleCodeMap.ToRegulatoryFeatures(1));                    // bit 1: declaredAgeRangeRequired
        Assert.Equal(RegulatoryFeatures.SignificantAppChangeRequiresAdultNotification, AppleCodeMap.ToRegulatoryFeatures(2)); // bit 2: adult notification
        Assert.Equal(RegulatoryFeatures.SignificantAppChangeRequiresParentalConsent, AppleCodeMap.ToRegulatoryFeatures(4));   // bit 4: parental consent
        Assert.Equal(RegulatoryFeatures.DeclaredAgeRangeRequired | RegulatoryFeatures.SignificantAppChangeRequiresParentalConsent, AppleCodeMap.ToRegulatoryFeatures(5));
        Assert.Equal(RegulatoryFeatures.None, AppleCodeMap.ToRegulatoryFeatures(8));                                  // unknown bit 8 is dropped
        Assert.Equal(RegulatoryFeatures.DeclaredAgeRangeRequired, AppleCodeMap.ToRegulatoryFeatures(9));             // known bit kept, unknown bit 8 dropped
    }

    [Theory]
    [InlineData(0, SignificantUpdateOutcome.Acknowledged)]  // 0: acknowledged
    [InlineData(1, SignificantUpdateOutcome.Unsupported)]   // 1: unsupported
    [InlineData(2, SignificantUpdateOutcome.Failed)]        // 2: failed
    [InlineData(3, SignificantUpdateOutcome.Failed)]        // unknown code
    [InlineData(-1, SignificantUpdateOutcome.Failed)]       // unknown code
    public void Apple_significant_update_codes(int code, SignificantUpdateOutcome expected) =>
        Assert.Equal(expected, AppleCodeMap.SignificantUpdate(code));
}
