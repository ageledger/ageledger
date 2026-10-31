using Ageledger;
using Microsoft.Extensions.DependencyInjection;

namespace AgeledgerSample;

/// <summary>
/// How the sample registers the library in a dependency-injection container. The app's own container (MauiProgram) and the container that the page
/// builds each time its settings change both go through <see cref="Register"/>, so a device run exercises the library's Android and iOS registrations.
/// </summary>
internal static class SampleServices
{
    internal static readonly string LogFile = Path.Combine(FileSystem.AppDataDirectory, "age-decisions.jsonl");

    /// <summary>Registers the options, the platform provider and backend and <see cref="AgeSignalsService"/>; on Android also the foreground activity.</summary>
    internal static void Register(IServiceCollection services, Action<AgeSignalsOptions> configure)
    {
        services.AddAgeledger(configure);
#if ANDROID
        services.AddAgeledgerActivity(() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
#endif
    }

    /// <summary>The options of the app's own container: the sample's saved settings, with the page's default values for the rest.</summary>
    internal static void ConfigureFromSavedSettings(AgeSignalsOptions options)
    {
        options.Enabled = MainPage.SafePref(MainPage.EnabledKey);
        options.AgeGates = [13, 16, 18];
        options.MinimumAge = 13;
        options.AppVersion = AppInfo.VersionString;
        options.RuleId = "TEST";
    }
}
