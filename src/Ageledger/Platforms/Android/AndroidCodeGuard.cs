using Google.Android.Play.AgeSignals;
using Google.Android.Play.AgeSignals.Model;

namespace Ageledger;

// Compile-time check that the binding's constants still have the numbers AndroidCodeMap maps. If a binding update changes one,
// the division by constant zero below stops the Android build (CS0020), instead of the mapping silently reporting wrong values.
internal static class AndroidCodeGuard
{
    private const bool Matches =
        AgeSignalsStatus.Unspecified == 0 && AgeSignalsStatus.Shared == 1 && AgeSignalsStatus.NotShared == 2 && AgeSignalsStatus.VerificationRequired == 3
        && SignificantChangeStatus.Unspecified == 0 && SignificantChangeStatus.Approved == 1 && SignificantChangeStatus.Pending == 2 && SignificantChangeStatus.Declined == 3
        && AgeSignalsErrorCode.NoError == 0 && AgeSignalsErrorCode.ApiNotAvailable == -1 && AgeSignalsErrorCode.PlayStoreNotFound == -2
        && AgeSignalsErrorCode.NetworkError == -3 && AgeSignalsErrorCode.PlayServicesNotFound == -4 && AgeSignalsErrorCode.CannotBindToService == -5
        && AgeSignalsErrorCode.PlayStoreVersionOutdated == -6 && AgeSignalsErrorCode.PlayServicesVersionOutdated == -7 && AgeSignalsErrorCode.ClientTransientError == -8
        && AgeSignalsErrorCode.AppNotOwned == -9 && AgeSignalsErrorCode.SdkVersionOutdated == -10 && AgeSignalsErrorCode.InternalError == -100;

    // Not used at run time; only its compile-time value matters.
    internal const int Check = 1 / (Matches ? 1 : 0);
}
