namespace Ageledger;

/// <summary>
/// Google's integer codes (the constants of the Play Age Signals binding) turned into the library's enums.
/// Kept apart from the Android-only backend so it compiles and is tested on net10.0. The numbers are Google's values
/// (checked against Xamarin.Google.Android.Play.Age.Signals 0.0.4); an unknown number maps to the "none/other" member.
/// </summary>
internal static class AndroidCodeMap
{
    internal static AndroidAccessStatus MapStatus(int? status) => status switch
    {
        1 => AndroidAccessStatus.Shared,                // AgeSignalsStatus.Shared
        2 => AndroidAccessStatus.NotShared,             // AgeSignalsStatus.NotShared
        3 => AndroidAccessStatus.VerificationRequired,  // AgeSignalsStatus.VerificationRequired
        _ => AndroidAccessStatus.Unspecified,           // AgeSignalsStatus.Unspecified (0), null or unknown
    };

    internal static AndroidSignificantChange MapChange(int? status) => status switch
    {
        1 => AndroidSignificantChange.Approved,   // SignificantChangeStatus.Approved
        2 => AndroidSignificantChange.Pending,    // SignificantChangeStatus.Pending
        3 => AndroidSignificantChange.Declined,   // SignificantChangeStatus.Declined
        _ => AndroidSignificantChange.None,       // SignificantChangeStatus.Unspecified (0), null or unknown
    };

    // AgeSignalsErrorCode.NoError (0) and any code Google adds later are reported as Other, as before.
    internal static AndroidErrorKind MapErrorCode(int code) => code switch
    {
        -1 => AndroidErrorKind.ApiNotAvailable,             // AgeSignalsErrorCode.ApiNotAvailable
        -9 => AndroidErrorKind.AppNotOwned,                 // AgeSignalsErrorCode.AppNotOwned
        -5 => AndroidErrorKind.CannotBindToService,         // AgeSignalsErrorCode.CannotBindToService
        -8 => AndroidErrorKind.ClientTransientError,        // AgeSignalsErrorCode.ClientTransientError
        -100 => AndroidErrorKind.InternalError,             // AgeSignalsErrorCode.InternalError
        -3 => AndroidErrorKind.NetworkError,                // AgeSignalsErrorCode.NetworkError
        -4 => AndroidErrorKind.PlayServicesNotFound,        // AgeSignalsErrorCode.PlayServicesNotFound
        -7 => AndroidErrorKind.PlayServicesVersionOutdated, // AgeSignalsErrorCode.PlayServicesVersionOutdated
        -2 => AndroidErrorKind.PlayStoreNotFound,           // AgeSignalsErrorCode.PlayStoreNotFound
        -6 => AndroidErrorKind.PlayStoreVersionOutdated,    // AgeSignalsErrorCode.PlayStoreVersionOutdated
        -10 => AndroidErrorKind.SdkVersionOutdated,         // AgeSignalsErrorCode.SdkVersionOutdated
        _ => AndroidErrorKind.Other,
    };
}
