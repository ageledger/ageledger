using Ageledger;
using Ageledger.Testing;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class PolicyAndServiceTests
{
    private static AgeSignalsOutcome Shared(int? lo, int? up) =>
        new() { Platform = AgeSignalsPlatform.Android, State = AgeSignalsState.Shared, Bucket = new(lo, up) };

    [Theory]
    [InlineData(0, 12, AgeDecision.Blocked)]
    [InlineData(13, 15, AgeDecision.AllowedWithParentalConsent)]
    [InlineData(16, 17, AgeDecision.AllowedWithParentalConsent)]
    [InlineData(18, null, AgeDecision.Allowed)]
    [InlineData(null, 12, AgeDecision.Blocked)]
    [InlineData(null, 17, AgeDecision.Blocked)]     // lower end unknown: treated as 0, the younger end
    [InlineData(12, 17, AgeDecision.Blocked)]       // may include someone under 13
    [InlineData(18, 24, AgeDecision.Allowed)]
    public void Default_policy(int? lo, int? up, AgeDecision expected) =>
        Assert.Equal(expected, DecisionPolicy.Decide(Shared(lo, up), On()));

    [Theory]
    [InlineData(13, 15, 13, 18, AgeDecision.AllowedWithParentalConsent)]
    [InlineData(16, 17, 18, 18, AgeDecision.Blocked)]               // lower 16 is below MinimumAge 18
    [InlineData(16, 17, 13, 16, AgeDecision.Allowed)]               // lower 16 is at AdultAge 16
    [InlineData(15, 17, 16, 18, AgeDecision.Blocked)]
    [InlineData(21, null, 13, 21, AgeDecision.Allowed)]
    public void Default_policy_uses_the_younger_end_with_custom_ages(int lo, int? up, int min, int adult, AgeDecision expected) =>
        Assert.Equal(expected, DecisionPolicy.Decide(Shared(lo, up), On(o => { o.MinimumAge = min; o.AdultAge = adult; })));

    [Fact]
    public void Custom_policy_wins()
    {
        var o = On(x => x.SharedRangePolicy = _ => AgeDecision.Allowed);
        Assert.Equal(AgeDecision.Allowed, DecisionPolicy.Decide(Shared(0, 12), o));
    }

    [Theory]
    [InlineData(AgeSignalsState.Declined, AgeDecision.NotApplicable)]
    [InlineData(AgeSignalsState.NotShared, AgeDecision.NotApplicable)]
    [InlineData(AgeSignalsState.Unavailable, AgeDecision.NotApplicable)]
    [InlineData(AgeSignalsState.Error, AgeDecision.NotApplicable)]
    [InlineData(AgeSignalsState.VerificationRequired, AgeDecision.Blocked)]
    public void Non_shared_states(AgeSignalsState state, AgeDecision expected) =>
        Assert.Equal(expected, DecisionPolicy.Decide(new() { Platform = AgeSignalsPlatform.iOS, State = state }, On()));

    [Fact]
    public void No_signal_and_verification_decisions_are_configurable()
    {
        var o = On(x => { x.NoSignalDecision = AgeDecision.Blocked; x.VerificationRequiredDecision = AgeDecision.AllowedWithParentalConsent; });
        Assert.Equal(AgeDecision.Blocked, DecisionPolicy.Decide(new() { Platform = AgeSignalsPlatform.iOS, State = AgeSignalsState.Declined }, o));
        Assert.Equal(AgeDecision.AllowedWithParentalConsent, DecisionPolicy.Decide(new() { Platform = AgeSignalsPlatform.Android, State = AgeSignalsState.VerificationRequired }, o));
    }

    // ---- feature flag ----

    [Fact]
    public void Sample_forced_options_are_accepted_by_an_enabled_service()
    {
        var options = new AgeSignalsOptions();
        AgeledgerSample.SampleForcedOptions.Apply(options);
        var (provider, _) = Android();
        var svc = new AgeSignalsService(options, provider);
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public void Everything_is_off_by_default() => Assert.False(new AgeSignalsOptions().Enabled);

    [Fact]
    public async Task Flag_off_makes_no_platform_call_and_logs_nothing()
    {
        var (provider, fake) = Android();
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(new AgeSignalsOptions(), provider, log);
        var e = await svc.EvaluateAsync();
        Assert.Equal(AgeSignalsState.Disabled, e.Result.State);
        Assert.Equal(AgeDecision.NotApplicable, e.Decision);
        Assert.Equal(0, fake.AccessCalls);
        Assert.Empty(await log.ReadAllAsync());
        Assert.Null(await svc.IsEligibleForAgeFeaturesAsync());
        Assert.Null(await svc.GetRequiredRegulatoryFeaturesAsync());
        Assert.Equal(SignificantUpdateOutcome.Unsupported, await svc.ShowSignificantUpdateAcknowledgmentAsync("x"));
    }

    [Fact]
    public async Task Flag_off_does_not_touch_apple_either()
    {
        var (provider, fake) = Apple();
        fake.Eligible = true;
        var svc = new AgeSignalsService(new AgeSignalsOptions(), provider, new InMemoryDecisionLog());
        await svc.EvaluateAsync();
        Assert.Equal(0, fake.RequestCalls);
        Assert.Null(await svc.IsEligibleForAgeFeaturesAsync());
    }

    [Fact]
    public async Task Flag_on_evaluates_and_logs_only_the_allowed_fields()
    {
        var (provider, fake) = Android();
        fake.SetNextAgeSignalsResult(16, 17);
        var log = new InMemoryDecisionLog();
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var svc = new AgeSignalsService(On(), provider, log, new FixedTime(now));
        var e = await svc.EvaluateAsync();
        Assert.Equal(AgeDecision.AllowedWithParentalConsent, e.Decision);
        var entry = Assert.Single(await log.ReadAllAsync());
        Assert.Equal(new DecisionLogEntry(now, "16-17", AgeDecision.AllowedWithParentalConsent, "1.2.3", "gate-v1"), entry);
    }

    [Fact]
    public async Task A_platform_exception_becomes_an_error_result_and_is_logged_without_its_text()
    {
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(), new ThrowingProvider(), log);
        var e = await svc.EvaluateAsync();
        Assert.Equal(AgeSignalsState.Error, e.Result.State);
        var json = await log.ExportJsonAsync();
        Assert.DoesNotContain("secret-device-id", json);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var svc = new AgeSignalsService(On(), new CancellingProvider(cts), new InMemoryDecisionLog());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.EvaluateAsync(cts.Token));
    }

    [Fact]
    public async Task A_cancellation_that_is_not_the_callers_maps_to_Error_Other_and_is_logged()
    {
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(), new CancellingProvider(null), log);
        var e = await svc.EvaluateAsync();
        Assert.Equal(AgeSignalsState.Error, e.Result.State);
        Assert.Equal(AgeSignalsError.Other, e.Result.Error);
        Assert.True(e.Logged);
        Assert.Single(await log.ReadAllAsync());
    }

    // ---- construction checks and log failures ----

    [Theory]
    [InlineData("1.4.2 (57)")]
    [InlineData("1.0.0-SNAPSHOT-build-with-a-very-long-suffix")]
    [InlineData("")]
    public void Invalid_app_version_throws_at_construction(string version)
    {
        var (provider, _) = Android();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AppVersion = version), provider));
    }

    [Theory]
    [InlineData("2027-gate")]
    [InlineData("")]
    [InlineData("cafebabe-dead-beef-abcd-0123456abcde")]
    public void Invalid_rule_id_throws_at_construction(string rule)
    {
        var (provider, _) = Android();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.RuleId = rule), provider));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    public void Out_of_range_age_gate_throws_at_construction(int gate)
    {
        var (provider, _) = Apple();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AgeGates = [13, gate]), provider));
    }

    [Fact]
    public void Descending_or_too_many_gates_throw_at_construction()
    {
        var (provider, _) = Apple();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AgeGates = [16, 13]), provider));
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AgeGates = [10, 13, 16, 18]), provider));
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.AgeGates = []), provider));
    }

    [Theory]
    [InlineData(13, 13)]
    [InlineData(18, 150)]
    [InlineData(16, 13)]
    public async Task Odd_android_bounds_are_logged_as_withheld_and_the_result_is_unchanged(int lower, int upper)
    {
        var (provider, fake) = Android();
        fake.SetNextAccessResult(AndroidAccessStatus.Shared);
        fake.SetNextAgeSignalsResult(lower, upper);
        var log = new InMemoryDecisionLog();
        var e = await new AgeSignalsService(On(), provider, log).EvaluateAsync();
        Assert.True(e.Logged);
        Assert.Equal(new AgeRangeBucket(lower, upper), e.Result.Bucket);
        Assert.Equal("withheld", Assert.Single(await log.ReadAllAsync()).AgeBucket);
    }

    [Fact]
    public async Task The_highest_valid_gate_still_logs()
    {
        var (provider, fake) = Apple();
        fake.NextResponse = new(AppleResponseKind.Sharing, 99, null);
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(o => o.AgeGates = [13, 99]), provider, log);
        var e = await svc.EvaluateAsync();
        Assert.True(e.Logged);
        Assert.Equal("99+", Assert.Single(await log.ReadAllAsync()).AgeBucket);
    }

    [Fact]
    public async Task A_throwing_log_still_returns_the_evaluation_with_Logged_false()
    {
        var (provider, fake) = Android();
        fake.SetNextAgeSignalsResult(18, null);
        var svc = new AgeSignalsService(On(), provider, new ThrowingLog());
        var e = await svc.EvaluateAsync();
        Assert.False(e.Logged);
        Assert.Equal(AgeDecision.Allowed, e.Decision);
        Assert.Equal(AgeSignalsState.Shared, e.Result.State);
    }

    [Fact]
    public async Task Logged_is_true_when_the_entry_was_written_and_false_without_a_log()
    {
        var (provider, _) = Android();
        Assert.True((await new AgeSignalsService(On(), provider, new InMemoryDecisionLog()).EvaluateAsync()).Logged);
        Assert.False((await new AgeSignalsService(On(), provider).EvaluateAsync()).Logged);
        Assert.False((await new AgeSignalsService(new AgeSignalsOptions(), provider, new InMemoryDecisionLog()).EvaluateAsync()).Logged);
    }

    [Fact]
    public async Task Options_changed_after_construction_are_ignored()
    {
        var (provider, _) = Android();
        var options = On();
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(options, provider, log);
        options.AppVersion = "1.4.2 (57)";
        options.Enabled = false;
        Assert.True(svc.IsEnabled);
        Assert.True((await svc.EvaluateAsync()).Logged);
        Assert.Equal("1.2.3", Assert.Single(await log.ReadAllAsync()).AppVersion);
    }

    private sealed class ThrowingLog : IDecisionLog
    {
        public Task AppendAsync(DecisionLogEntry entry, CancellationToken ct = default) => throw new IOException("disk full at /secret/path");
        public Task<IReadOnlyList<DecisionLogEntry>> ReadAllAsync(CancellationToken ct = default) => throw new IOException();
        public Task<string> ExportJsonAsync(CancellationToken ct = default) => throw new IOException();
        public Task ClearAsync(CancellationToken ct = default) => throw new IOException();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class ThrowingProvider : IAgeSignalsProvider
    {
        public AgeSignalsPlatform Platform => AgeSignalsPlatform.Android;
        public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken ct = default) => throw new InvalidOperationException("secret-device-id");
        public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken ct = default) => Task.FromResult<bool?>(null);
        public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken ct = default) => Task.FromResult<RegulatoryFeatures?>(null);
        public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string d, CancellationToken ct = default) => Task.FromResult(SignificantUpdateOutcome.Unsupported);
    }

    private sealed class CancellingProvider(CancellationTokenSource? cts) : IAgeSignalsProvider
    {
        public AgeSignalsPlatform Platform => AgeSignalsPlatform.iOS;
        public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken ct = default)
        {
            cts?.Cancel();
            throw new OperationCanceledException(cts?.Token ?? default);
        }
        public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken ct = default) => Task.FromResult<bool?>(null);
        public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken ct = default) => Task.FromResult<RegulatoryFeatures?>(null);
        public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string d, CancellationToken ct = default) => Task.FromResult(SignificantUpdateOutcome.Unsupported);
    }
}
