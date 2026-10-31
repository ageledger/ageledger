#if DEBUG
using Ageledger;
using Microsoft.Extensions.DependencyInjection;

namespace AgeledgerSample;

internal sealed partial class SelfTestRun
{
    /// <summary>
    /// D1: the service from the app's own container (built in MauiProgram with AddAgeledger, AddAgeledgerFileLog and, on Android, AddAgeledgerActivity).
    /// It must resolve (on Android with the saved Enabled setting, which also needs the activity registration), be the same instance each time, report the saved Enabled
    /// setting, and sit on the provider of this platform. A second check builds a separate container through the same <see cref="SampleServices.Register"/> with
    /// Enabled forced on and only resolves the service, so on Android a missing activity registration throws. Nothing is evaluated, so no platform call is made and
    /// no log entry is written; the saved settings are not read for the second check or changed.
    /// </summary>
    private Obs CheckAppContainer()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null) return Obs.Failed("no-container");
        var service = services.GetRequiredService<AgeSignalsService>();
        var options = services.GetRequiredService<AgeSignalsOptions>();
        var provider = services.GetRequiredService<IAgeSignalsProvider>();
        var expectedPlatform = platform.Name == "android" ? AgeSignalsPlatform.Android : AgeSignalsPlatform.iOS;
        var savedEnabled = MainPage.SafePref(MainPage.EnabledKey);
        var forced = ResolvesWhenEnabled(expectedPlatform);
        var ok = forced && ReferenceEquals(service, services.GetRequiredService<AgeSignalsService>())
            && service.IsEnabled == options.Enabled && service.IsEnabled == savedEnabled
            && provider.Platform == expectedPlatform;
        return Obs.Check(ok, $"enabled={(service.IsEnabled ? 1 : 0)} platform={(int)provider.Platform} forced={(forced ? 1 : 0)}");
    }

    /// <summary>Builds a container through <see cref="SampleServices.Register"/> with Enabled forced on (<see cref="SampleForcedOptions"/>, no log) and resolves the service without evaluating.</summary>
    private static bool ResolvesWhenEnabled(AgeSignalsPlatform expectedPlatform)
    {
        try
        {
            var collection = new ServiceCollection();
            SampleServices.Register(collection, SampleForcedOptions.Apply);
            using var provider = collection.BuildServiceProvider();
            var forced = provider.GetRequiredService<AgeSignalsService>();
            return forced.IsEnabled && provider.GetRequiredService<IAgeSignalsProvider>().Platform == expectedPlatform;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
#endif
