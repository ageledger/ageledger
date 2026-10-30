using Ageledger;
#if ANDROID
using Android.App;
#endif
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers <see cref="AgeSignalsService"/> and what it needs in a dependency-injection container.
/// Every registration uses <c>TryAdd</c>, so a service you registered before calling these methods is kept.
/// </summary>
/// <remarks>
/// The service stays off until <see cref="AgeSignalsOptions.Enabled"/> is set to true. This only wires objects together; it does not make an app compliant with any law.
/// What <see cref="AddAgeledger"/> registers, by target:
/// Android and iOS get the platform provider and backend; every other target (plain net10.0, including Mac Catalyst and Windows heads) gets <see cref="NoAgeSignalsProvider"/>, which reports
/// <see cref="AgeSignalsState.Unavailable"/> / <see cref="AgeSignalsError.NotAvailable"/> without any platform call.
/// No decision log is registered unless you call <see cref="AddAgeledgerFileLog"/> or register an <see cref="IDecisionLog"/> yourself: without one the service logs nothing.
/// The options and the service are singletons, and the service keeps what it is given: the provider, the backend, the <see cref="IDecisionLog"/>, the options and the <see cref="TimeProvider"/>
/// must all be singletons, including any you register yourself.
/// The options are built, and checked by the service, when the service is first resolved: resolve <see cref="AgeSignalsService"/> once at start-up to find a bad setting early.
/// </remarks>
public static class AgeledgerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the options (a new <see cref="AgeSignalsOptions"/> with <paramref name="configure"/> applied when first resolved), the platform provider and backend,
    /// and <see cref="AgeSignalsService"/>. No decision log is registered.
    /// </summary>
    /// <remarks>
    /// More than one call is allowed: every <paramref name="configure"/> is kept and they all run, in the order of the calls, on the one options instance; everything else is registered once.
    /// If you register your own <see cref="AgeSignalsOptions"/> instance, before or after any call to this method, that instance is the one used and no <paramref name="configure"/> is invoked.
    /// Register everything before the container is built.
    /// If a <paramref name="configure"/> throws, resolving the options (and so the service) fails and nothing is cached: a later resolve runs every configure again on a new options instance.
    /// On Android also call <c>AddAgeledgerActivity</c>; the service refuses to be created enabled without it.
    /// </remarks>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">Sets the options. Null leaves the defaults (off).</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAgeledger(this IServiceCollection services, Action<AgeSignalsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        // One marker holds every configure from every call, so a later call adds to the options instead of being dropped.
        var configuration = services.FirstOrDefault(d => d.ServiceType == typeof(AgeledgerConfiguration))?.ImplementationInstance as AgeledgerConfiguration;
        if (configuration is null)
        {
            configuration = new AgeledgerConfiguration();
            services.AddSingleton(configuration);
        }
        if (configure is not null) configuration.Actions.Add(configure);
        services.TryAddSingleton(sp =>
        {
            var options = new AgeSignalsOptions();
            foreach (var action in sp.GetRequiredService<AgeledgerConfiguration>().Actions) action(options);
            return options;
        });
#if ANDROID
        services.TryAddSingleton<IAndroidAgeSignalsBackend>(sp =>
        {
            var source = sp.GetService<AgeledgerActivitySource>();
            if (source is null && sp.GetRequiredService<AgeSignalsOptions>().Enabled)
                throw new InvalidOperationException("On Android call services.AddAgeledgerActivity(() => <current activity>) so the consent screen has an activity.");
            return new GoogleAgeSignalsBackend(source?.Get ?? (() => null));
        });
        services.TryAddSingleton<IAgeSignalsProvider>(sp => new AndroidAgeSignalsProvider(sp.GetRequiredService<IAndroidAgeSignalsBackend>()));
#elif IOS
        services.TryAddSingleton<IAppleAgeRangeBackend>(_ => new AppleAgeRangeBackend());
        services.TryAddSingleton<IAgeSignalsProvider>(sp => new AppleAgeSignalsProvider(sp.GetRequiredService<IAppleAgeRangeBackend>()));
#else
        services.TryAddSingleton<IAgeSignalsProvider>(_ => new NoAgeSignalsProvider());
#endif
        services.TryAddSingleton(sp => new AgeSignalsService(
            sp.GetRequiredService<AgeSignalsOptions>(),
            sp.GetRequiredService<IAgeSignalsProvider>(),
            sp.GetService<IDecisionLog>(),
            sp.GetService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// Registers a <see cref="JsonFileDecisionLog"/> at <paramref name="path"/> (default limits: 1000 entries, 365 days) unless an <see cref="IDecisionLog"/> is already registered.
    /// It uses the container's <see cref="TimeProvider"/> if there is one, so the log and the service share a clock.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="path">The log file, for example under the app's private data directory. The folder is created when the first entry is written.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or white space.</exception>
    public static IServiceCollection AddAgeledgerFileLog(this IServiceCollection services, string path)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        services.TryAddSingleton<IDecisionLog>(sp => new JsonFileDecisionLog(path, time: sp.GetService<TimeProvider>()));
        return services;
    }

#if ANDROID
    /// <summary>
    /// Android: registers the function that returns the foreground activity (in a MAUI app usually <c>() =&gt; Microsoft.Maui.ApplicationModel.Platform.CurrentActivity</c>;
    /// the library itself has no MAUI dependency). It is called on the main thread. If the service is enabled and this was not called,
    /// resolving the service throws <see cref="InvalidOperationException"/>. When it is disabled the activity is not needed.
    /// A second call keeps the first function.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="currentActivity">Returns the foreground activity, or null when there is none.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="currentActivity"/> is null.</exception>
    public static IServiceCollection AddAgeledgerActivity(this IServiceCollection services, Func<Activity?> currentActivity)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(currentActivity);
        services.TryAddSingleton(new AgeledgerActivitySource(currentActivity));
        return services;
    }

    // Wraps the delegate so no bare Func<Activity?> is registered in the app's container.
    internal sealed record AgeledgerActivitySource(Func<Activity?> Get);
#endif

    // Every configure callback from every AddAgeledger call, in call order. Internal: it only lives in the container so later calls can add to it.
    internal sealed class AgeledgerConfiguration
    {
        public List<Action<AgeSignalsOptions>> Actions { get; } = [];
    }
}
