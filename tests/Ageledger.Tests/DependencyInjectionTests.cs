using Ageledger;
using Ageledger.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class DependencyInjectionTests
{
    // The test project references only the abstractions package (no extra dependency), so this is a minimal container for singleton registrations.
    // It refuses any other lifetime, so a registration that is not a singleton fails the test.
    // The real container, with the checks MAUI's container can run: every registration must be buildable and no singleton may capture a scoped service.
    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static readonly Action<AgeSignalsOptions> Enable = o => { o.Enabled = true; o.AppVersion = "1.0.0"; o.RuleId = "gate-v1"; };

    private static ServiceCollection WithFakeProvider(FakeAppleAgeRangeBackend? fake = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgeSignalsProvider>(new AppleAgeSignalsProvider(fake ?? new FakeAppleAgeRangeBackend()));
        return services;
    }

    [Fact]
    public void Every_descriptor_is_a_singleton_and_the_service_resolves_once()
    {
        var services = new ServiceCollection();
        services.AddAgeledger();
        services.AddAgeledgerFileLog(TempFile());
        Assert.NotEmpty(services);
        Assert.All(services, d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));
        var sp = Build(services);
        Assert.Same(sp.GetRequiredService<AgeSignalsService>(), sp.GetRequiredService<AgeSignalsService>());
        Assert.Same(sp.GetRequiredService<AgeSignalsOptions>(), sp.GetRequiredService<AgeSignalsOptions>());
    }

    [Fact]
    public async Task Defaults_are_off_and_make_no_platform_call()
    {
        var fake = new FakeAppleAgeRangeBackend();
        var services = WithFakeProvider(fake);
        services.AddAgeledger();
        var sp = Build(services);
        Assert.False(sp.GetRequiredService<AgeSignalsOptions>().Enabled);
        var service = sp.GetRequiredService<AgeSignalsService>();
        Assert.False(service.IsEnabled);
        var e = await service.EvaluateAsync();
        Assert.Equal(AgeSignalsState.Disabled, e.Result.State);
        Assert.Equal(AgeDecision.NotApplicable, e.Decision);
        Assert.False(e.Logged);
        Assert.Equal(0, fake.RequestCalls);
    }

    [Fact]
    public void Configure_is_applied()
    {
        var services = WithFakeProvider();
        services.AddAgeledger(o => { Enable(o); o.AppVersion = "2.0.1"; o.RuleId = "gate-v2"; o.MinimumAge = 14; });
        var sp = Build(services);
        var options = sp.GetRequiredService<AgeSignalsOptions>();
        Assert.True(options.Enabled);
        Assert.Equal("2.0.1", options.AppVersion);
        Assert.Equal("gate-v2", options.RuleId);
        Assert.Equal(14, options.MinimumAge);
        Assert.True(sp.GetRequiredService<AgeSignalsService>().IsEnabled);
    }

    [Fact]
    public void An_invalid_enabled_setting_fails_when_the_service_is_resolved()
    {
        var services = WithFakeProvider();
        services.AddAgeledger(o => { o.Enabled = true; o.AppVersion = "1.4.2 (57)"; });
        var sp = Build(services);
        Assert.Throws<ArgumentException>(() => { sp.GetRequiredService<AgeSignalsService>(); });
    }

    [Fact]
    public void A_prior_options_registration_wins_and_configure_is_not_called()
    {
        var mine = On();
        var called = false;
        var services = WithFakeProvider();
        services.AddSingleton(mine);
        services.AddAgeledger(_ => called = true);
        var sp = Build(services);
        Assert.Same(mine, sp.GetRequiredService<AgeSignalsOptions>());
        Assert.True(sp.GetRequiredService<AgeSignalsService>().IsEnabled);
        Assert.False(called);
    }

    [Fact]
    public async Task A_prior_provider_registration_wins()
    {
        var mine = new FakeAppleAgeRangeBackend { NextResponse = new AppleAgeRangeResponse(AppleResponseKind.Sharing, 13, 15) };
        var services = WithFakeProvider(mine);
        services.AddAgeledger(Enable);
        var sp = Build(services);
        var e = await sp.GetRequiredService<AgeSignalsService>().EvaluateAsync();
        Assert.Equal("13-15", e.Result.Bucket.Label);
        Assert.Equal(1, mine.RequestCalls);
    }

    [Fact]
    public void A_prior_service_registration_wins()
    {
        var mine = new AgeSignalsService(new AgeSignalsOptions(), new AppleAgeSignalsProvider(new FakeAppleAgeRangeBackend()));
        var services = WithFakeProvider();
        services.AddSingleton(mine);
        services.AddAgeledger();
        Assert.Same(mine, Build(services).GetRequiredService<AgeSignalsService>());
    }

    [Fact]
    public void A_second_call_keeps_the_first_configure_and_adds_no_descriptors()
    {
        var first = 0;
        var second = 0;
        var services = WithFakeProvider();
        services.AddAgeledger(_ => first++);
        var count = services.Count;
        services.AddAgeledger(_ => second++);
        Assert.Equal(count, services.Count);
        var sp = Build(services);
        sp.GetRequiredService<AgeSignalsService>();
        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task The_unsupported_provider_reports_unavailable_when_enabled_and_logs_unknown()
    {
        var services = new ServiceCollection();
        var log = new InMemoryDecisionLog();
        services.AddSingleton<IDecisionLog>(log);
        services.AddAgeledger(Enable);   // no provider registered: the plain net10.0 build adds the unsupported one
        var sp = Build(services);
        var service = sp.GetRequiredService<AgeSignalsService>();
        var e = await service.EvaluateAsync();
        Assert.Equal(AgeSignalsPlatform.None, e.Result.Platform);
        Assert.Equal(AgeSignalsState.Unavailable, e.Result.State);
        Assert.Equal(AgeSignalsError.NotAvailable, e.Result.Error);
        Assert.Equal(new AgeSignalsOptions().NoSignalDecision, e.Decision);
        Assert.True(e.Logged);
        var row = Assert.Single(await log.ReadAllAsync());
        Assert.Equal("unknown", row.AgeBucket);
        Assert.Null(await service.IsEligibleForAgeFeaturesAsync());
        Assert.Null(await service.GetRequiredRegulatoryFeaturesAsync());
        Assert.Equal(SignificantUpdateOutcome.Unsupported, await service.ShowSignificantUpdateAcknowledgmentAsync("What changed"));
    }

    [Fact]
    public async Task No_log_is_registered_by_default_and_a_registered_log_is_used()
    {
        var services = WithFakeProvider();
        services.AddAgeledger(Enable);
        var sp = Build(services);
        Assert.Null(sp.GetService<IDecisionLog>());
        Assert.False((await sp.GetRequiredService<AgeSignalsService>().EvaluateAsync()).Logged);

        var log = new InMemoryDecisionLog();
        var services2 = WithFakeProvider();
        services2.AddSingleton<IDecisionLog>(log);
        services2.AddAgeledger(Enable);
        Assert.True((await Build(services2).GetRequiredService<AgeSignalsService>().EvaluateAsync()).Logged);
        Assert.Single(await log.ReadAllAsync());
    }

    [Fact]
    public async Task The_file_log_shares_the_containers_clock_ahead_of_real_time()
    {
        var path = TempFile();
        var now = DateTimeOffset.UtcNow.AddDays(2);
        var services = WithFakeProvider();
        services.AddSingleton<TimeProvider>(new FixedTime(now));
        services.AddAgeledger(Enable);
        services.AddAgeledgerFileLog(path);
        var sp = Build(services);
        Assert.IsType<JsonFileDecisionLog>(sp.GetRequiredService<IDecisionLog>());
        Assert.True((await sp.GetRequiredService<AgeSignalsService>().EvaluateAsync()).Logged);
        var row = Assert.Single(await sp.GetRequiredService<IDecisionLog>().ReadAllAsync());
        Assert.Equal(now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), row.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public async Task The_file_log_shares_the_containers_clock_behind_real_time()
    {
        var path = TempFile();
        var services = WithFakeProvider();
        services.AddSingleton<TimeProvider>(new FixedTime(DateTimeOffset.UtcNow.AddDays(-730)));
        services.AddAgeledger(Enable);
        services.AddAgeledgerFileLog(path);
        var sp = Build(services);
        Assert.True((await sp.GetRequiredService<AgeSignalsService>().EvaluateAsync()).Logged);
        // Retention is measured on the shared clock, so a row stamped by it is kept.
        Assert.Single(await sp.GetRequiredService<IDecisionLog>().ReadAllAsync());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void The_file_log_keeps_a_prior_log()
    {
        var mine = new InMemoryDecisionLog();
        var services = WithFakeProvider();
        services.AddSingleton<IDecisionLog>(mine);
        services.AddAgeledgerFileLog(TempFile());
        Assert.Same(mine, Build(services).GetRequiredService<IDecisionLog>());
    }

    [Fact]
    public async Task A_fixed_clock_reaches_the_service()
    {
        var log = new InMemoryDecisionLog();
        var now = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var services = WithFakeProvider();
        services.AddSingleton<TimeProvider>(new FixedTime(now));
        services.AddSingleton<IDecisionLog>(log);
        services.AddAgeledger(Enable);
        await Build(services).GetRequiredService<AgeSignalsService>().EvaluateAsync();
        Assert.Equal(now, Assert.Single(await log.ReadAllAsync()).TimestampUtc);
    }

    [Fact]
    public void Argument_checks()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddAgeledger());
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddAgeledgerFileLog("x"));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAgeledgerFileLog(" "));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddAgeledgerFileLog(null!));
    }
}
