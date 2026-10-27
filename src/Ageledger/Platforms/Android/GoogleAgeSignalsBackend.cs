using Android.App;
using Android.Content;
using Android.Gms.Extensions;
using Google.Android.Play.AgeSignals;
using Google.Android.Play.AgeSignals.Model;

namespace Ageledger;

/// <summary>
/// Wraps Microsoft's binding (Xamarin.Google.Android.Play.Age.Signals) without rebinding anything.
/// Google's installId and ageRangeSource are read by the SDK but dropped here.
/// </summary>
public sealed class GoogleAgeSignalsBackend : IAndroidAgeSignalsBackend
{
    private readonly Lazy<IAgeSignalsManager> _manager;
    private readonly Func<Activity?> _activity;

    /// <summary>Creates the backend over Google's manager (created on the first request).</summary>
    /// <param name="activity">
    /// Returns the foreground activity (in a MAUI app: <c>() =&gt; Microsoft.Maui.ApplicationModel.Platform.CurrentActivity</c>). It is called on the main thread.
    /// Null, a finishing or a destroyed activity is reported as <see cref="AndroidErrorKind.NoActivity"/> (<see cref="AgeSignalsError.NoPresenter"/>).
    /// </param>
    /// <param name="context">Application context; defaults to <see cref="Application.Context"/>.</param>
    /// <param name="manager">Inject Google's <c>FakeAgeSignalsManager</c> here for on-device tests; null uses the real manager.</param>
    public GoogleAgeSignalsBackend(Func<Activity?> activity, Context? context = null, IAgeSignalsManager? manager = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activity = activity;
        // Created on the first request, so an app with the feature flag off never touches Play. PublicationOnly: a failed creation is retried on the next call, not cached.
        _manager = new(() => manager ?? AgeSignalsManagerFactory.Create(context ?? Application.Context), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <inheritdoc/>
    public async Task<AndroidAccessOutcome> RequestAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // The activity is read and the request started on the main looper; the wait for Google's answer happens off it.
            var pending = await OnMainLooperAsync(() =>
            {
                var activity = _activity();
                if (activity is null || activity.IsFinishing || activity.IsDestroyed) return null;
                var request = AgeSignalsAccessRequest.InvokeBuilder()!.SetActivity(activity)!.Build()!;
                return _manager.Value.RequestAgeSignalsAccess(request)!.AsAsync<AgeSignalsAccessResult>();
            }, cancellationToken).ConfigureAwait(false);
            if (pending is null) return new(AndroidAccessStatus.Unspecified, AndroidErrorKind.NoActivity);
            pending.ObserveFaults();
            var result = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(AndroidCodeMap.MapStatus(result?.AgeSignalsStatus()?.IntValue()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(AndroidAccessStatus.Unspecified, MapError(ex));
        }
    }

    // Runs work on the main looper (directly when already there) and returns what it produced.
    private static Task<T?> OnMainLooperAsync<T>(Func<T?> work, CancellationToken cancellationToken) where T : class
    {
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.Task.ObserveFaults();
        void Run()
        {
            if (cancellationToken.IsCancellationRequested) { tcs.TrySetCanceled(cancellationToken); return; }
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }
        var main = Android.OS.Looper.MainLooper!;
        if (main.IsCurrentThread) Run(); else new Android.OS.Handler(main).Post(Run);
        return tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AndroidCheckOutcome> CheckAgeSignalsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var request = AgeSignalsRequest.InvokeBuilder()!.Build()!;
            var check = _manager.Value.CheckAgeSignals(request)!.AsAsync<AgeSignalsResult>();
            check.ObserveFaults();
            var result = await check.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result is null) return new(null, null, AndroidSignificantChange.None);
            return new(
                result.AgeLower()?.IntValue(),
                result.AgeUpper()?.IntValue(),
                AndroidCodeMap.MapChange(result.SignificantChangeStatus()?.IntValue()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(null, null, AndroidSignificantChange.None, MapError(ex));
        }
    }

    internal static AndroidErrorKind MapError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is AgeSignalsException ase)
                return AndroidCodeMap.MapErrorCode(ase.ErrorCode);
            if (e.InnerException is null) break;
        }
        return AndroidErrorKind.Other;
    }
}
