using Android.App;
using Android.Gms.Extensions;
using Google.Android.Play.AgeSignals;
using Google.Android.Play.AgeSignals.Model;
#if DEBUG
using Google.Android.Play.AgeSignals.Testing;
#endif
using Ageledger;
using Switch = Microsoft.Maui.Controls.Switch;
using Platform = Microsoft.Maui.ApplicationModel.Platform;

namespace AgeledgerSample;

public sealed partial class MainPage
{
#if DEBUG
    private readonly Switch useRealManager = new() { IsToggled = SafePref(UseRealManagerKey) };
    private readonly Switch nullActivity = new();
    private FakeAgeSignalsManager fake = new();

    private static readonly (string Name, int Code)[] ErrorCodes =
    [
        ("(none)", -1000),
        ("NetworkError", AgeSignalsErrorCode.NetworkError), ("ApiNotAvailable", AgeSignalsErrorCode.ApiNotAvailable),
        ("AppNotOwned", AgeSignalsErrorCode.AppNotOwned), ("PlayStoreNotFound", AgeSignalsErrorCode.PlayStoreNotFound),
        ("PlayServicesNotFound", AgeSignalsErrorCode.PlayServicesNotFound), ("CannotBindToService", AgeSignalsErrorCode.CannotBindToService),
        ("PlayStoreVersionOutdated", AgeSignalsErrorCode.PlayStoreVersionOutdated),
        ("PlayServicesVersionOutdated", AgeSignalsErrorCode.PlayServicesVersionOutdated),
        ("SdkVersionOutdated", AgeSignalsErrorCode.SdkVersionOutdated),
        ("ClientTransientError", AgeSignalsErrorCode.ClientTransientError), ("InternalError", AgeSignalsErrorCode.InternalError),
    ];

    private readonly Picker accessResult = new() { ItemsSource = new[] { "Shared", "NotShared", "VerificationRequired" }, SelectedIndex = 0 };
    private readonly Entry ageLower = new() { Text = "13", Placeholder = "lower", Keyboard = Keyboard.Numeric };
    private readonly Entry ageUpper = new() { Text = "15", Placeholder = "upper", Keyboard = Keyboard.Numeric };
    private readonly Picker significantChange = new() { ItemsSource = new[] { "None", "Pending", "Approved", "Declined" }, SelectedIndex = 0 };
    private readonly Picker exceptionCode = new() { ItemsSource = ErrorCodes.Select(e => e.Name).ToList(), SelectedIndex = 0 };
    private readonly Picker exceptionOn = new() { ItemsSource = new[] { "Access call (SetNextRequestAgeSignalsAccessException)", "Check call (SetNextAgeSignalsException)", "Both" }, SelectedIndex = 0 };
#endif

    private partial IAgeSignalsProvider CreateProvider()
    {
#if DEBUG
        if (!useRealManager.IsToggled)
        {
            Func<Activity?> activity = nullActivity.IsToggled ? () => null : () => Platform.CurrentActivity;
            return new AndroidAgeSignalsProvider(new GoogleAgeSignalsBackend(activity, manager: fake));
        }
#endif
        return new AndroidAgeSignalsProvider(new GoogleAgeSignalsBackend(() => Platform.CurrentActivity));
    }

    private partial void AddPlatformControls()
    {
#if DEBUG
        Add("Use real Play manager (off = Fake)", useRealManager);
        useRealManager.Toggled += (_, e) => { SetPref(UseRealManagerKey, e.Value); Rebuild(); };
        Add("Null-activity backend (A16)", nullActivity);
        nullActivity.Toggled += (_, _) => Rebuild();

        root.Add(new Label { Text = "Fake scenario (Apply resets the Fake first)", FontAttributes = FontAttributes.Bold });
        Add("Access result", accessResult);
        Add("Age lower (blank = none)", ageLower);
        Add("Age upper (blank = none)", ageUpper);
        Add("Significant change", significantChange);
        Add("Exception", exceptionCode);
        Add("Exception on", exceptionOn);
        AddButton("Apply scenario", ApplyScenario);
        RestoreScenario();
        AddButton("Diagnostic: call Fake directly (type names only)", Diagnose);
#endif
    }

#if DEBUG
    private static Java.Lang.Integer Int(int v) => Java.Lang.Integer.ValueOf(v)!;

    private static Java.Lang.Integer? OptionalInt(string? text) => int.TryParse(text, out var v) ? Int(v) : null;

    private FakeAgeSignalsManager ConfigureFake()
    {
        var fake = new FakeAgeSignalsManager();
        var access = accessResult.SelectedIndex switch
        {
            1 => AgeSignalsStatus.NotShared,
            2 => AgeSignalsStatus.VerificationRequired,
            _ => AgeSignalsStatus.Shared,
        };
        fake.SetNextAgeSignalsAccessResult(AgeSignalsAccessResult.InvokeBuilder()!.SetAgeSignalsStatus(Int(access))!.Build()!);

        var builder = AgeSignalsResult.InvokeBuilder()!;
        if (OptionalInt(ageLower.Text) is { } lo) builder.SetAgeLower(lo);
        if (OptionalInt(ageUpper.Text) is { } up) builder.SetAgeUpper(up);
        var change = significantChange.SelectedIndex switch
        {
            1 => SignificantChangeStatus.Pending,
            2 => SignificantChangeStatus.Approved,
            3 => SignificantChangeStatus.Declined,
            _ => (int?)null,
        };
        if (change is { } c) builder.SetSignificantChangeStatus(Int(c));
        fake.SetNextAgeSignalsResult(builder.Build()!);

        var code = ErrorCodes[Math.Max(0, exceptionCode.SelectedIndex)].Code;
        if (code != -1000)
        {
            var on = exceptionOn.SelectedIndex;
            if (on is 0 or 2) fake.SetNextRequestAgeSignalsAccessException(new AgeSignalsException(code));
            if (on is 1 or 2) fake.SetNextAgeSignalsException(new AgeSignalsException(code));
        }
        return fake;
    }

    private const string ScenarioKey = "fake-scenario";

    /// <summary>Re-applies the last applied Fake scenario (before the first Rebuild, so an evaluation at launch uses it).</summary>
    private void RestoreScenario()
    {
        try
        {
            var saved = Preferences.Get(ScenarioKey, "");
            if (saved.Length == 0) return;
            var p = saved.Split('|');
            if (p.Length != 7) return;
            accessResult.SelectedIndex = int.Parse(p[0]);
            ageLower.Text = p[1];
            ageUpper.Text = p[2];
            significantChange.SelectedIndex = int.Parse(p[3]);
            exceptionCode.SelectedIndex = int.Parse(p[4]);
            exceptionOn.SelectedIndex = int.Parse(p[5]);
            fake = ConfigureFake();
        }
        catch (Exception ex) { startupNote = " Scenario not restored: " + ex.GetType().Name + "."; }
    }

    private void SaveScenario()
    {
        SetPref(ScenarioKey, string.Join('|', accessResult.SelectedIndex, ageLower.Text, ageUpper.Text, significantChange.SelectedIndex,
            exceptionCode.SelectedIndex, exceptionOn.SelectedIndex, "v1"));
    }

    private void ApplyScenario()
    {
        fake = ConfigureFake();
        SaveScenario();
        if (Rebuild()) status.Text += " Fake scenario applied.";
    }

    /// <summary>Calls the Fake directly and shows only exception type names, never messages. Nothing is written to the log.</summary>
    private async Task Diagnose()
    {
        var lines = new List<string>();
        var fake = ConfigureFake(); // its own instance, so the service's queued values are not consumed
        try
        {
            var request = AgeSignalsAccessRequest.InvokeBuilder()!.SetActivity(Platform.CurrentActivity!)!.Build()!;
            await fake.RequestAgeSignalsAccess(request)!.AsAsync<AgeSignalsAccessResult>();
            lines.Add("access: ok");
        }
        catch (Exception ex) { lines.Add("access: " + TypeNames(ex)); }
        try
        {
            await fake.CheckAgeSignals(AgeSignalsRequest.InvokeBuilder()!.Build()!)!.AsAsync<AgeSignalsResult>();
            lines.Add("check: ok");
        }
        catch (Exception ex) { lines.Add("check: " + TypeNames(ex)); }
        result.Text = string.Join("\n", lines);
    }

    private static string TypeNames(Exception ex)
    {
        var names = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException) names.Add(e.GetType().Name);
        return string.Join(" > ", names);
    }
#endif
}
