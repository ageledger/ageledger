using Ageledger;
#if ANDROID
using Android.App;
#endif
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers <see cref="AgeSignalsService"/> and what it needs in a dependency-injection container.
/// Every registration uses <c>TryAdd</c>, so a service you registered before calling <c>AddAgeledger</c> is kept.
/// Call <c>AddAgeledger</c> once: options are registered by the first call, a later call does not apply its <c>configure</c> callback.
/// </summary>
/// <remarks>
/// The service stays off until <see cref="AgeSignalsOptions.Enabled"/> is set to true. This only wires objects together;
/// it does not make an app compliant with any law.
/// What is registered, by target:
/// Android and iOS get the platform provider and backend (Android needs the foreground activity, see the overload that takes it);
/// plain net10.0 has no platform, so register an <see cref="IAgeSignalsProvider"/> yourself (for example one over the fakes in the Ageledger.Testing package) before resolving the service.
/// No decision log is registered unless you use the overload that takes a file path, or register an <see cref="IDecisionLog"/> yourself: without one the service logs nothing.
/// An optional <see cref="TimeProvider"/> in the container is passed to the service.
/// </remarks>
public static class AgeledgerServiceCollectionExtensions
{
    /// <summary>Registers the same as <see cref="AddAgeledger(IServiceCollection, Action{AgeSignalsOptions}?)"/> with the default options (off).</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAgeledger(this IServiceCollection services) => services.AddAgeledger((Action<AgeSignalsOptions>?)null);

    /// <summary>
    /// Registers the options (a new <see cref="AgeSignalsOptions"/> with <paramref name="configure"/> applied when first resolved), the platform provider and backend,
    /// and <see cref="AgeSignalsService"/> as a singleton. No decision log is registered.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">Sets the options (null leaves the defaults). Not called if <see cref="AgeSignalsOptions"/> was already registered.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAgeledger(this IServiceCollection services, Action<AgeSignalsOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(_ =>
        {
            var options = new AgeSignalsOptions();
            configure?.Invoke(options);
            return options;
        });
#if ANDROID
        services.TryAddSingleton<IAndroidAgeSignalsBackend>(sp => new GoogleAgeSignalsBackend(sp.GetService<Func<Activity?>>() ?? (() => null)));
        services.TryAddSingleton<IAgeSignalsProvider>(sp => new AndroidAgeSignalsProvider(sp.GetRequiredService<IAndroidAgeSignalsBackend>()));
#elif IOS
        services.TryAddSingleton<IAppleAgeRangeBackend>(_ => new AppleAgeRangeBackend());
        services.TryAddSingleton<IAgeSignalsProvider>(sp => new AppleAgeSignalsProvider(sp.GetRequiredService<IAppleAgeRangeBackend>()));
#endif
        services.TryAddSingleton(sp => new AgeSignalsService(
            sp.GetRequiredService<AgeSignalsOptions>(),
            sp.GetRequiredService<IAgeSignalsProvider>(),
            sp.GetService<IDecisionLog>(),
            sp.GetService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// Like <see cref="AddAgeledger(IServiceCollection, Action{AgeSignalsOptions}?)"/>, and also registers a <see cref="JsonFileDecisionLog"/> at <paramref name="decisionLogPath"/>
    /// (default limits: 1000 entries, 365 days) unless an <see cref="IDecisionLog"/> is already registered.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="decisionLogPath">The log file, for example under the app's private data directory. The file is created when the first entry is written.</param>
    /// <param name="configure">Sets the options (null leaves the defaults). Not called if <see cref="AgeSignalsOptions"/> was already registered.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="decisionLogPath"/> is null, empty or white space.</exception>
    public static IServiceCollection AddAgeledger(this IServiceCollection services, string decisionLogPath, Action<AgeSignalsOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionLogPath);
        services.TryAddSingleton<IDecisionLog>(_ => new JsonFileDecisionLog(decisionLogPath));
        return services.AddAgeledger(configure);
    }

#if ANDROID
    /// <summary>
    /// Android: like <see cref="AddAgeledger(IServiceCollection, Action{AgeSignalsOptions}?)"/>, with the function that returns the foreground activity
    /// (in a MAUI app: <c>() =&gt; Microsoft.Maui.ApplicationModel.Platform.CurrentActivity</c>). Without it the backend reports
    /// <see cref="AgeSignalsError.NoPresenter"/> when asked. It is called on the main thread.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="currentActivity">Returns the foreground activity, or null when there is none.</param>
    /// <param name="configure">Sets the options. Not called if <see cref="AgeSignalsOptions"/> was already registered.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="currentActivity"/> is null.</exception>
    public static IServiceCollection AddAgeledger(this IServiceCollection services, Func<Activity?> currentActivity, Action<AgeSignalsOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(currentActivity);
        services.TryAddSingleton(currentActivity);
        return services.AddAgeledger(configure);
    }
#endif
}
