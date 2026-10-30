using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace AgeledgerSample;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
#if DEBUG
    // A Debug build can be asked to run the on-device self-test by intent extras (scripts/selftest-android.sh). Release builds read no extras.
    // The extras count only on a fresh start from the host: not when the activity is recreated (saved state) or reopened from the recents list,
    // and they are removed once read.
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var intent = Intent;
        var fresh = savedInstanceState is null && intent is not null && !intent.Flags.HasFlag(ActivityFlags.LaunchedFromHistory);
        SelfTestLaunch.Current = fresh ? SelfTestLaunch.Create(intent!.GetStringExtra(SelfTestLaunch.SuiteName), intent.GetStringExtra(SelfTestLaunch.NonceName)) : null;
        intent?.RemoveExtra(SelfTestLaunch.SuiteName);
        intent?.RemoveExtra(SelfTestLaunch.NonceName);
        base.OnCreate(savedInstanceState);
    }
#endif
}
