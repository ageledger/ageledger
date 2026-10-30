using Ageledger;
using Ageledger.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class DependencyInjectionTests
{
    // The test project references only the abstractions package (no extra dependency), so this is a minimal container for singleton registrations.
    private sealed class Container(ServiceCollection services) : IServiceProvider, IDisposable
    {
        private readonly Dictionary<Type, object> _made = [];

        public object? GetService(Type type)
        {
            if (type == typeof(IServiceProvider)) return this;
            if (_made.TryGetValue(type, out var made)) return made;
            var d = services.LastOrDefault(x => x.ServiceType == type);
            if (d is null) return null;
            var value = d.ImplementationInstance ?? d.ImplementationFactory!(this);
            _made[type] = value;
            return value;
        }

        public void Dispose() { }
    }

    private static Container Build(ServiceCollection services) => new(services);

    private static ServiceCollection WithFakeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgeSignalsProvider>(new AppleAgeSignalsProvider(new FakeAppleAgeRangeBackend()));
        return services;
    }

    [Fact]
    public void Resolves_the_service_as_a_singleton()
    {
        var services = WithFakeProvider();
        services.AddAgeledger();
        using var sp = Build(services);
        Assert.Same(sp.GetRequiredService<AgeSignalsService>(), sp.GetRequiredService<AgeSignalsService>());
    }

    [Fact]
    public void Enabled_is_false_by_default()
    {
        var services = WithFakeProvider();
        services.AddAgeledger();
        using var sp = Build(services);
        Assert.False(sp.GetRequiredService<AgeSignalsOptions>().Enabled);
        Assert.False(sp.GetRequiredService<AgeSignalsService>().IsEnabled);
    }

    [Fact]
    public async Task A_default_service_is_disabled_and_makes_no_platform_call()
    {
        var fake = new FakeAppleAgeRangeBackend();
        var services = new ServiceCollection();
        services.AddSingleton<IAgeSignalsProvider>(new AppleAgeSignalsProvider(fake));
        services.AddAgeledger();
        using var sp = Build(services);
        var e = await sp.GetRequiredService<AgeSignalsService>().EvaluateAsync();
        Assert.Equal(AgeSignalsState.Disabled, e.Result.State);
        Assert.Equal(0, fake.RequestCalls);
    }

    [Fact]
    public void Configure_is_applied()
    {
        var services = WithFakeProvider();
        services.AddAgeledger(o => { o.Enabled = true; o.AppVersion = "2.0.1"; o.RuleId = "gate-v2"; o.MinimumAge = 14; });
        using var sp = Build(services);
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
        using var sp = Build(services);
        Assert.Throws<ArgumentException>(() => { sp.GetRequiredService<AgeSignalsService>(); });
    }

    [Fact]
    public void A_prior_options_registration_wins_and_configure_is_not_called()
    {
        var mine = On();
        var called = false;
        var services = WithFakeProvider();
        services.AddSingleton(mine);
        services.AddAgeledger((Action<AgeSignalsOptions>)(_ => called = true));
        using var sp = Build(services);
        Assert.Same(mine, sp.GetRequiredService<AgeSignalsOptions>());
        Assert.True(sp.GetRequiredService<AgeSignalsService>().IsEnabled);
        Assert.False(called);
    }

    [Fact]
    public async Task A_prior_provider_registration_wins()
    {
        var mine = new FakeAppleAgeRangeBackend { NextResponse = new AppleAgeRangeResponse(AppleResponseKind.Sharing, 13, 15) };
        var services = new ServiceCollection();
        services.AddSingleton<IAgeSignalsProvider>(new AppleAgeSignalsProvider(mine));
        services.AddAgeledger(o => { o.Enabled = true; o.AppVersion = "1.0.0"; o.RuleId = "gate-v1"; });
        using var sp = Build(services);
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
        using var sp = Build(services);
        Assert.Same(mine, sp.GetRequiredService<AgeSignalsService>());
    }

    [Fact]
    public async Task No_log_is_registered_by_default_and_a_registered_log_is_used()
    {
        var services = WithFakeProvider();
        services.AddAgeledger(o => { o.Enabled = true; o.AppVersion = "1.0.0"; o.RuleId = "gate-v1"; });
        using (var sp = Build(services))
        {
            Assert.Null(sp.GetService<IDecisionLog>());
            Assert.False((await sp.GetRequiredService<AgeSignalsService>().EvaluateAsync()).Logged);
        }

        var log = new InMemoryDecisionLog();
        var services2 = WithFakeProvider();
        services2.AddSingleton<IDecisionLog>(log);
        services2.AddAgeledger(o => { o.Enabled = true; o.AppVersion = "1.0.0"; o.RuleId = "gate-v1"; });
        using var sp2 = Build(services2);
        Assert.True((await sp2.GetRequiredService<AgeSignalsService>().EvaluateAsync()).Logged);
        Assert.Single(await log.ReadAllAsync());
    }

    [Fact]
    public async Task The_path_overload_registers_a_file_log()
    {
        var path = TempFile();
        var services = WithFakeProvider();
        services.AddAgeledger(path, o => { o.Enabled = true; o.AppVersion = "1.0.0"; o.RuleId = "gate-v1"; });
        using var sp = Build(services);
        Assert.IsType<JsonFileDecisionLog>(sp.GetRequiredService<IDecisionLog>());
        Assert.True((await sp.GetRequiredService<AgeSignalsService>().EvaluateAsync()).Logged);
        Assert.True(File.Exists(path));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void The_path_overload_keeps_a_prior_log()
    {
        var mine = new InMemoryDecisionLog();
        var services = WithFakeProvider();
        services.AddSingleton<IDecisionLog>(mine);
        services.AddAgeledger(TempFile(), null);
        using var sp = Build(services);
        Assert.Same(mine, sp.GetRequiredService<IDecisionLog>());
    }

    [Fact]
    public void Argument_checks()
    {
        Assert.Throws<ArgumentNullException>(() => DependencyInjectionExtensionsCall(null!));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAgeledger(" ", null));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddAgeledger((string)null!, null));
    }

    private static void DependencyInjectionExtensionsCall(IServiceCollection services) => services.AddAgeledger();

    [Fact]
    public void Without_a_platform_provider_plain_net_needs_one_registered()
    {
        var services = new ServiceCollection();
        services.AddAgeledger();
        using var sp = Build(services);
        Assert.Throws<InvalidOperationException>(() => { sp.GetRequiredService<AgeSignalsService>(); });
    }
}
