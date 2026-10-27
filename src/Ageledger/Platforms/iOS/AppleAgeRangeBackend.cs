using Ageledger.Native;
using UIKit;

namespace Ageledger;

/// <summary>
/// Apple Declared Age Range through the Swift wrapper in native/ios. Every call checks the OS version first
/// (requestAgeRange 26.0, isEligibleForAgeFeatures 26.2, regulatory features and significant-update acknowledgment 26.4).
/// The app needs the <c>com.apple.developer.declared-age-range</c> entitlement.
/// </summary>
public sealed class AppleAgeRangeBackend : IAppleAgeRangeBackend
{
    private readonly Func<UIViewController?> _presenter;

    /// <summary>Creates the backend over the Swift wrapper.</summary>
    /// <param name="presenter">
    /// Returns the view controller to present Apple's sheet from. It is called on the main thread. Null uses the top view controller
    /// of the foreground window scene (an active one, then an inactive foreground one; never a background scene, which gives <see cref="AppleErrorKind.NoPresenter"/>).
    /// </param>
    public AppleAgeRangeBackend(Func<UIViewController?>? presenter = null) => _presenter = presenter ?? TopViewController;

    /// <inheritdoc/>
    public Task<AppleAgeRangeResponse> RequestAgeRangeAsync(IReadOnlyList<int> ageGates, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsIOSVersionAtLeast(26, 0))
            return Task.FromResult(new AppleAgeRangeResponse(AppleResponseKind.Error, Error: AppleErrorKind.NotAvailable));

        var tcs = new TaskCompletionSource<AppleAgeRangeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        // UIKit objects (scenes, windows, view controllers) are read only on the main thread, so the presenter is resolved there too.
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (cancellationToken.IsCancellationRequested) { tcs.TrySetCanceled(cancellationToken); return; }
            try
            {
                var presenter = _presenter();
                if (presenter is null)
                {
                    tcs.TrySetResult(new AppleAgeRangeResponse(AppleResponseKind.Error, Error: AppleErrorKind.NoPresenter));
                    return;
                }
                MASAgeSignalsBridge.RequestAgeRange(
                    ageGates[0],
                    ageGates.Count > 1 ? Foundation.NSNumber.FromInt32(ageGates[1]) : null,
                    ageGates.Count > 2 ? Foundation.NSNumber.FromInt32(ageGates[2]) : null,
                    presenter,
                    r => tcs.TrySetResult(Convert(r)));
            }
            catch (Exception)
            {
                tcs.TrySetResult(new AppleAgeRangeResponse(AppleResponseKind.Error, Error: AppleErrorKind.Other));
            }
        });
        // Cancelling stops the wait; Apple's sheet itself cannot be dismissed from here.
        return tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsIOSVersionAtLeast(26, 2)) return Task.FromResult<bool?>(null);
        var tcs = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        MASAgeSignalsBridge.IsEligibleForAgeFeatures(v => tcs.TrySetResult(AppleCodeMap.Eligible((int)v)));
        return tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsIOSVersionAtLeast(26, 4)) return Task.FromResult<RegulatoryFeatures?>(null);
        var tcs = new TaskCompletionSource<RegulatoryFeatures?>(TaskCreationOptions.RunContinuationsAsynchronously);
        MASAgeSignalsBridge.RequiredRegulatoryFeatures(v => tcs.TrySetResult(AppleCodeMap.ToRegulatoryFeatures((int)v)));
        return tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsIOSVersionAtLeast(26, 4)) return Task.FromResult(SignificantUpdateOutcome.Unsupported);
        var tcs = new TaskCompletionSource<SignificantUpdateOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (cancellationToken.IsCancellationRequested) { tcs.TrySetCanceled(cancellationToken); return; }
            try
            {
                var scene = ForegroundScene();
                if (scene is null) { tcs.TrySetResult(SignificantUpdateOutcome.Failed); return; }
                MASAgeSignalsBridge.ShowSignificantUpdateAcknowledgment(scene, updateDescription, v =>
                    tcs.TrySetResult(AppleCodeMap.SignificantUpdate((int)v)));
            }
            catch (Exception)
            {
                tcs.TrySetResult(SignificantUpdateOutcome.Failed);
            }
        });
        return tcs.Task.WaitAsync(cancellationToken);
    }

    internal static AppleAgeRangeResponse Convert(MASAgeRangeResult r) => new(
        AppleCodeMap.ResponseKind((int)r.Kind),
        r.LowerBound?.Int32Value,
        r.UpperBound?.Int32Value,
        AppleCodeMap.ToParentalControls((int)r.ParentalControls),
        AppleCodeMap.ErrorKind((int)r.ErrorKind));

    // Main thread only. The active foreground scene, then an inactive foreground one. No fallback to a background scene (a sheet cannot be shown from it): null.
    private static UIWindowScene? ForegroundScene()
    {
        var scenes = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().ToList();
        return scenes.FirstOrDefault(s => s.ActivationState == UISceneActivationState.ForegroundActive)
            ?? scenes.FirstOrDefault(s => s.ActivationState == UISceneActivationState.ForegroundInactive);
    }

    // Main thread only.
    private static UIViewController? TopViewController()
    {
        var windows = ForegroundScene()?.Windows;
        var window = windows?.FirstOrDefault(w => w.IsKeyWindow) ?? windows?.FirstOrDefault(w => !w.Hidden && w.RootViewController is not null);
        var vc = window?.RootViewController;
        while (vc?.PresentedViewController is { } next) vc = next;
        return vc;
    }
}
