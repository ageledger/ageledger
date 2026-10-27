using Ageledger;
using Ageledger.Testing;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class Step6HardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancelled() => new(true);

    // ---- pre-cancelled token ----

    [Fact]
    public async Task A_cancelled_token_throws_before_any_android_call_and_logs_nothing()
    {
        var (provider, fake) = Android();
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(), provider, log);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.EvaluateAsync(Cancelled()));
        Assert.Equal(0, fake.AccessCalls);
        Assert.Equal(0, fake.CheckCalls);
        Assert.Empty(await log.ReadAllAsync());
    }

    [Fact]
    public async Task A_cancelled_token_throws_before_any_apple_call_and_logs_nothing()
    {
        var (provider, fake) = Apple();
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(), provider, log);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.EvaluateAsync(Cancelled()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.IsEligibleForAgeFeaturesAsync(Cancelled()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.GetRequiredRegulatoryFeaturesAsync(Cancelled()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.ShowSignificantUpdateAcknowledgmentAsync("What changed", Cancelled()));
        Assert.Equal(0, fake.RequestCalls);
        Assert.Null(fake.LastUpdateDescription);
        Assert.Empty(await log.ReadAllAsync());
    }

    [Fact]
    public async Task The_providers_also_refuse_a_cancelled_token_before_the_backend()
    {
        var (android, androidFake) = Android();
        var (apple, appleFake) = Apple();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => android.RequestAgeSignalsAsync(On(), Cancelled()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apple.RequestAgeSignalsAsync(On(), Cancelled()));
        Assert.Equal(0, androidFake.AccessCalls);
        Assert.Equal(0, appleFake.RequestCalls);
    }

    [Fact]
    public async Task A_cancelled_token_on_a_disabled_service_still_throws()
    {
        var (provider, _) = Android();
        var svc = new AgeSignalsService(new AgeSignalsOptions(), provider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.EvaluateAsync(Cancelled()));
    }

    // ---- RequestTimeout ----

    [Fact]
    public void The_timeout_defaults_to_none() => Assert.Null(new AgeSignalsOptions().RequestTimeout);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(600_001)]
    public void A_timeout_outside_zero_to_ten_minutes_throws_when_enabled_and_is_ignored_when_disabled(int ms)
    {
        var (provider, _) = Android();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(ms)), provider));
        _ = new AgeSignalsService(new AgeSignalsOptions { RequestTimeout = TimeSpan.FromMilliseconds(ms) }, provider);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(600_000)]
    public void A_timeout_inside_the_range_is_accepted(int ms)
    {
        var (provider, _) = Android();
        _ = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(ms)), provider);
    }

    [Fact]
    public async Task A_timeout_gives_Error_Timeout_and_the_decision_is_logged()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(50)), probe, log);
        var e = await svc.EvaluateAsync();
        Assert.Equal(AgeSignalsState.Error, e.Result.State);
        Assert.Equal(AgeSignalsError.Timeout, e.Result.Error);
        Assert.Equal(AgeDecision.NotApplicable, e.Decision);
        Assert.True(e.Logged);
        var row = Assert.Single(await log.ReadAllAsync());
        Assert.Equal("unknown", row.AgeBucket);
        Assert.False(probe.SawCancelableToken);
    }

    [Fact]
    public async Task The_callers_cancellation_during_a_timed_call_throws_and_logs_nothing()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMinutes(5)), probe, log);
        using var cts = new CancellationTokenSource();
        var task = svc.EvaluateAsync(cts.Token);
        await probe.Started.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(await log.ReadAllAsync());
    }

    [Fact]
    public async Task Without_a_timeout_a_call_that_finishes_is_unaffected()
    {
        var (provider, fake) = Android();
        fake.SetNextAgeSignalsResult(18, null);
        var e = await new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromSeconds(30)), provider).EvaluateAsync();
        Assert.Equal(AgeSignalsState.Shared, e.Result.State);
    }

    // ---- Android NoActivity ----

    [Fact]
    public async Task NoActivity_maps_to_Error_NoPresenter_and_no_check_follows()
    {
        var (provider, fake) = Android();
        fake.SetNextAccessError(AndroidErrorKind.NoActivity);
        var r = await provider.RequestAgeSignalsAsync(On());
        Assert.Equal(AgeSignalsState.Error, r.State);
        Assert.Equal(AgeSignalsError.NoPresenter, r.Error);
        Assert.Equal(AgeSignalsPlatform.Android, r.Platform);
        Assert.Equal(0, fake.CheckCalls);
    }

    // ---- single flight ----

    [Fact]
    public async Task Concurrent_evaluations_on_one_service_never_overlap_in_the_provider()
    {
        var probe = new ProbeProvider { Delay = TimeSpan.FromMilliseconds(30) };
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(), probe, log);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => svc.EvaluateAsync()));
        Assert.Equal(1, probe.MaxConcurrency);
        Assert.Equal(6, probe.Calls);
        Assert.All(results, r => Assert.True(r.Logged));
        Assert.Equal(6, (await log.ReadAllAsync()).Count);
    }

    [Fact]
    public async Task A_waiting_caller_can_cancel_without_reaching_the_provider()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var svc = new AgeSignalsService(On(), probe);
        using var first = new CancellationTokenSource();
        var firstTask = svc.EvaluateAsync(first.Token);
        await probe.Started.Task;
        using var second = new CancellationTokenSource();
        var secondTask = svc.EvaluateAsync(second.Token);
        second.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask);
        Assert.Equal(1, probe.Calls);
        first.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstTask);
        // the lock is free again; the abandoned request is still the one in flight, so finish it and the next call gets its answer
        probe.Gate.SetResult();
        Assert.True((await svc.EvaluateAsync()).Result.State == AgeSignalsState.NotShared);
    }

    // ---- options snapshot and policy ----

    [Fact]
    public async Task Changing_the_gates_array_or_the_options_after_construction_has_no_effect()
    {
        var (provider, fake) = Apple();
        fake.NextResponse = new(AppleResponseKind.Sharing, 13, 15);
        var gates = new[] { 13, 16, 18 };
        var o = On(x => x.AgeGates = gates);
        var svc = new AgeSignalsService(o, provider, new InMemoryDecisionLog());
        gates[1] = 13;
        o.AgeGates = [13, 13];
        o.MinimumAge = 50;
        o.NoSignalDecision = AgeDecision.Blocked;
        var e = await svc.EvaluateAsync();
        Assert.Equal(AgeSignalsState.Shared, e.Result.State);
        Assert.Equal(AgeDecision.AllowedWithParentalConsent, e.Decision);
        Assert.Equal([13, 16, 18], fake.LastGates);
    }

    [Fact]
    public void The_service_has_no_public_way_to_reach_its_options() =>
        Assert.Null(typeof(AgeSignalsService).GetProperty("Options"));

    [Theory]
    [InlineData(7, 0)]
    [InlineData(0, 7)]
    public void Undefined_decision_options_throw_when_enabled(int noSignal, int verification)
    {
        var (provider, _) = Android();
        Assert.Throws<ArgumentException>(() => new AgeSignalsService(On(o =>
        {
            o.NoSignalDecision = (AgeDecision)noSignal;
            o.VerificationRequiredDecision = (AgeDecision)verification;
        }), provider));
        _ = new AgeSignalsService(new AgeSignalsOptions { NoSignalDecision = (AgeDecision)noSignal, VerificationRequiredDecision = (AgeDecision)verification }, provider);
    }

    [Fact]
    public async Task A_throwing_range_policy_propagates_and_nothing_is_logged()
    {
        var (provider, fake) = Android();
        fake.SetNextAgeSignalsResult(18, null);
        var log = new InMemoryDecisionLog();
        var first = true;
        var svc = new AgeSignalsService(On(o => o.SharedRangePolicy = _ => { if (first) { first = false; throw new FormatException("policy bug"); } return AgeDecision.Allowed; }), provider, log);
        await Assert.ThrowsAsync<FormatException>(() => svc.EvaluateAsync());
        Assert.Empty(await log.ReadAllAsync());
        // the lock was released: the next evaluation completes
        Assert.Equal(AgeDecision.Allowed, (await svc.EvaluateAsync().WaitAsync(TimeSpan.FromSeconds(5))).Decision);
    }

    [Fact]
    public async Task A_range_policy_returning_an_undefined_decision_throws_InvalidOperationException()
    {
        var (provider, fake) = Android();
        fake.SetNextAgeSignalsResult(18, null);
        var log = new InMemoryDecisionLog();
        var first = true;
        var svc = new AgeSignalsService(On(o => o.SharedRangePolicy = _ => { if (first) { first = false; return (AgeDecision)42; } return AgeDecision.Allowed; }), provider, log);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EvaluateAsync());
        Assert.Empty(await log.ReadAllAsync());
        Assert.Equal(AgeDecision.Allowed, (await svc.EvaluateAsync().WaitAsync(TimeSpan.FromSeconds(5))).Decision);
    }

    // ---- pass-through methods ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_update_description_is_refused_before_the_platform(string? text)
    {
        var (provider, fake) = Apple();
        var svc = new AgeSignalsService(On(), provider);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.ShowSignificantUpdateAcknowledgmentAsync(text!));
        Assert.Null(fake.LastUpdateDescription);
    }

    [Fact]
    public async Task An_update_description_over_the_limit_is_refused_and_the_limit_itself_is_accepted()
    {
        var (provider, fake) = Apple();
        var svc = new AgeSignalsService(On(), provider);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ShowSignificantUpdateAcknowledgmentAsync(new string('x', 1001)));
        Assert.Equal(SignificantUpdateOutcome.Acknowledged, await svc.ShowSignificantUpdateAcknowledgmentAsync(new string('x', 1000)));
        Assert.Equal(1000, fake.LastUpdateDescription!.Length);
    }

    [Fact]
    public async Task Platform_failures_in_the_pass_through_methods_give_null_or_Failed_not_exceptions()
    {
        var svc = new AgeSignalsService(On(), new ProbeProvider { ThrowOnOtherCalls = true });
        Assert.Null(await svc.IsEligibleForAgeFeaturesAsync());
        Assert.Null(await svc.GetRequiredRegulatoryFeaturesAsync());
        Assert.Equal(SignificantUpdateOutcome.Failed, await svc.ShowSignificantUpdateAcknowledgmentAsync("What changed"));
    }

    [Fact]
    public async Task A_disabled_service_pass_through_methods_make_no_call()
    {
        var (provider, fake) = Apple();
        fake.Eligible = true;
        var svc = new AgeSignalsService(new AgeSignalsOptions(), provider);
        Assert.Null(await svc.IsEligibleForAgeFeaturesAsync());
        Assert.Null(await svc.GetRequiredRegulatoryFeaturesAsync());
        Assert.Equal(SignificantUpdateOutcome.Unsupported, await svc.ShowSignificantUpdateAcknowledgmentAsync("What changed"));
        Assert.Null(fake.LastUpdateDescription);
    }

    [Fact]
    public async Task The_pass_through_methods_return_what_the_provider_says()
    {
        var (provider, fake) = Apple();
        fake.Eligible = true;
        fake.Features = RegulatoryFeatures.DeclaredAgeRangeRequired;
        var svc = new AgeSignalsService(On(), provider);
        Assert.True(await svc.IsEligibleForAgeFeaturesAsync());
        Assert.Equal(RegulatoryFeatures.DeclaredAgeRangeRequired, await svc.GetRequiredRegulatoryFeaturesAsync());
    }

    // ---- decision logs ----

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_001)]
    public void MaxEntries_outside_1_to_100000_throws(int maxEntries) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonFileDecisionLog(TempFile(), maxEntries));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.9)]
    [InlineData(3650.5)]
    public void Retention_outside_1_to_3650_days_throws(double days) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonFileDecisionLog(TempFile(), retention: TimeSpan.FromDays(days)));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100_000, 3650)]
    public void The_limits_themselves_are_accepted(int maxEntries, int days) =>
        _ = new JsonFileDecisionLog(TempFile(), maxEntries, TimeSpan.FromDays(days));

    [Fact]
    public async Task Appending_null_throws_in_both_logs_and_stores_nothing()
    {
        var memory = new InMemoryDecisionLog();
        await Assert.ThrowsAsync<ArgumentNullException>(() => memory.AppendAsync(null!));
        Assert.Empty(await memory.ReadAllAsync());
        Assert.Equal("[]", await memory.ExportJsonAsync());

        var path = TempFile();
        try
        {
            var file = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await Assert.ThrowsAsync<ArgumentNullException>(() => file.AppendAsync(null!));
            Assert.False(File.Exists(path));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task An_entry_more_than_a_day_in_the_future_is_refused_on_append_and_dropped_on_read()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await Assert.ThrowsAsync<ArgumentException>(() => log.AppendAsync(Entry(Now.AddDays(1).AddSeconds(5))));
            await log.AppendAsync(Entry(Now.AddHours(23)));   // within the allowed skew
            File.AppendAllText(path, Line("2026-10-26T12:00:00Z") + "\n" + Line("2026-10-24T12:00:00Z", "18+") + "\n");
            Assert.Equal(["13-15", "18+"], (await log.ReadAllAsync()).Select(e => e.AgeBucket));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task A_line_over_512_bytes_is_dropped_and_one_of_exactly_512_is_kept()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var ok = Line();
            var padded = ok + new string(' ', 512 - ok.Length);          // exactly 512 bytes of JSON plus trailing spaces
            var tooLong = ok + new string(' ', 513 - ok.Length);
            Assert.Equal(512, padded.Length);
            File.WriteAllText(path, tooLong + "\n" + Line(bucket: "18+") + "\n" + padded + "\n" + new string('x', 100_000) + "\n" + Line(bucket: "0-12"));
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            Assert.Equal(["18+", "13-15", "0-12"], (await log.ReadAllAsync()).Select(e => e.AgeBucket));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task A_byte_order_mark_on_the_first_line_is_still_skipped()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, Line() + "\n", new System.Text.UTF8Encoding(true));
            Assert.Single(await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Append_writes_the_exact_bytes_and_leaves_no_temporary_file()
    {
        var path = TempFile();
        try
        {
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            await log.AppendAsync(Entry(Now));
            await log.AppendAsync(Entry(Now, "18+"));
            Assert.Equal(Line() + "\n" + Line(bucket: "18+") + "\n", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally { Cleanup(path); }
    }


    // ---- abandoned requests (M1, M2, N5) ----

    [Fact]
    public async Task After_a_timeout_the_next_evaluation_waits_for_the_same_request_instead_of_starting_another()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(50)), probe);
        Assert.Equal(AgeSignalsError.Timeout, (await svc.EvaluateAsync()).Result.Error);
        Assert.Equal(AgeSignalsError.Timeout, (await svc.EvaluateAsync()).Result.Error);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(1, probe.MaxConcurrency);
    }

    [Fact]
    public async Task A_request_that_completes_after_the_first_call_timed_out_answers_the_second_call()
    {
        var probe = new ProbeProvider { NeverCompletes = true, Bucket = new AgeRangeBucket(13, 15) };
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(50)), probe, log);
        Assert.Equal(AgeSignalsError.Timeout, (await svc.EvaluateAsync()).Result.Error);
        // The first call released the lock before it returned, so this call acquires it and joins the pending request before EvaluateAsync returns its task.
        var second = svc.EvaluateAsync();
        probe.Gate.SetResult();
        var e = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AgeSignalsState.Shared, e.Result.State);
        Assert.Equal(1, probe.Calls);
        // one sheet, two rows: the first call's Timeout row (unknown), then the answer row
        Assert.Equal(["unknown", "13-15"], (await log.ReadAllAsync()).Select(r => r.AgeBucket));
        // the answer was consumed: a third call asks the platform again
        probe.NeverCompletes = false;
        await svc.EvaluateAsync();
        Assert.Equal(2, probe.Calls);
        Assert.False(probe.SawCancelableToken);
    }

    [Fact]
    public async Task A_caller_cancel_also_leaves_the_request_for_the_next_call()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var svc = new AgeSignalsService(On(), probe);
        using var cts = new CancellationTokenSource();
        var first = svc.EvaluateAsync(cts.Token);
        await probe.Started.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = svc.EvaluateAsync();
        probe.Gate.SetResult();
        Assert.Equal(AgeSignalsState.NotShared, (await second.WaitAsync(TimeSpan.FromSeconds(5))).Result.State);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task A_fault_after_the_caller_stopped_waiting_never_reaches_UnobservedTaskException()
    {
        var marker = "late-fault-" + Guid.NewGuid().ToString("N");
        var raised = false;
        void Handler(object? s, UnobservedTaskExceptionEventArgs a)
        {
            if (a.Exception.ToString().Contains(marker)) raised = true;
        }
        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            var probe = new ProbeProvider { NeverCompletes = true };
            var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(30)), probe);
            Assert.Equal(AgeSignalsError.Timeout, (await svc.EvaluateAsync()).Result.Error);
            probe.Gate.SetException(new InvalidOperationException(marker));
            await Task.Delay(100);
            probe = null;
            svc = null;
            for (var i = 0; i < 4; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(20); }
        }
        finally { TaskScheduler.UnobservedTaskException -= Handler; }
        Assert.False(raised);
    }

    [Fact]
    public async Task A_significant_update_call_does_not_reach_the_backend_while_a_timed_out_evaluations_request_is_unfinished()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(50)), probe);
        Assert.Equal(AgeSignalsError.Timeout, (await svc.EvaluateAsync()).Result.Error);
        var show = svc.ShowSignificantUpdateAcknowledgmentAsync("What changed");
        await Task.Delay(150);
        Assert.Equal(0, probe.ShowCalls);
        Assert.False(show.IsCompleted);
        probe.Gate.SetResult();
        Assert.Equal(SignificantUpdateOutcome.Unsupported, await show.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, probe.ShowCalls);
    }

    [Fact]
    public async Task After_a_cancelled_significant_update_call_an_evaluation_waits_for_that_sheet_and_then_asks()
    {
        var probe = new ProbeProvider { ShowNeverCompletes = true };
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(80)), probe);
        using var cts = new CancellationTokenSource();
        var show = svc.ShowSignificantUpdateAcknowledgmentAsync("What changed", cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => show);
        Assert.Equal(1, probe.ShowCalls);
        // the sheet is still tracked: the evaluation waits for it (bounded by the timeout) and does not call the platform
        Assert.Equal(AgeSignalsError.Timeout, (await svc.EvaluateAsync()).Result.Error);
        Assert.Equal(0, probe.Calls);
        probe.ShowGate.SetResult();
        Assert.Equal(AgeSignalsState.NotShared, (await svc.EvaluateAsync().WaitAsync(TimeSpan.FromSeconds(5))).Result.State);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task The_timeout_also_bounds_the_wait_for_an_earlier_request_and_both_calls_are_logged()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(100)), probe, log);
        var results = await Task.WhenAll(svc.EvaluateAsync(), svc.EvaluateAsync());
        Assert.All(results, r => Assert.Equal(AgeSignalsError.Timeout, r.Result.Error));
        Assert.All(results, r => Assert.True(r.Logged));
        Assert.Equal(2, (await log.ReadAllAsync()).Count);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task An_answer_that_arrives_while_no_call_has_joined_is_discarded_and_the_next_call_asks_again()
    {
        var provider = new TcsProvider();
        var log = new InMemoryDecisionLog();
        var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMilliseconds(50)), provider, log);
        Assert.Equal(AgeSignalsError.Timeout, (await svc.EvaluateAsync()).Result.Error);
        // completing the source marks the task completed at once, so no delay is needed before the next call
        provider.Sources[0].SetResult(new() { Platform = AgeSignalsPlatform.Android, State = AgeSignalsState.Shared, Bucket = new AgeRangeBucket(18, null) });
        provider.AutoComplete = true;
        var e = await svc.EvaluateAsync();
        Assert.Equal(AgeSignalsState.NotShared, e.Result.State);
        Assert.Equal(2, provider.Sources.Count);
        var rows = await log.ReadAllAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(["unknown", "unknown"], rows.Select(r => r.AgeBucket));   // the discarded "18+" answer was never logged
    }

    [Fact]
    public async Task A_timeout_that_fires_as_the_pending_request_ends_does_not_start_a_new_request()
    {
        for (var i = 0; i < 100; i++)
        {
            var probe = new ProbeProvider { ShowNeverCompletes = true };
            var clock = new ManualTime();
            var svc = new AgeSignalsService(On(o => o.RequestTimeout = TimeSpan.FromMinutes(1)), probe, new InMemoryDecisionLog(), clock);
            using var cts = new CancellationTokenSource();
            var show = svc.ShowSignificantUpdateAcknowledgmentAsync("What changed", cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => show);
            var evaluation = svc.EvaluateAsync();     // holds the service and waits for the sheet to end
            probe.ShowGate.SetResult();               // the sheet ends ...
            clock.FireAll();                          // ... and the timeout fires at the same moment
            var e = await evaluation.WaitAsync(TimeSpan.FromSeconds(5));
            if (e.Result.Error == AgeSignalsError.Timeout) Assert.Equal(0, probe.Calls);   // a timeout never starts a new request
            Assert.True(probe.Calls <= 1);
        }
    }

    [Fact]
    public async Task A_waiting_significant_update_call_can_be_cancelled()
    {
        var probe = new ProbeProvider { NeverCompletes = true };
        var svc = new AgeSignalsService(On(), probe);
        _ = svc.EvaluateAsync();
        await probe.Started.Task;
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.ShowSignificantUpdateAcknowledgmentAsync("What changed", cts.Token));
        Assert.Equal(0, probe.ShowCalls);
    }

    // ---- file log: future rows and clocks (S3, S4) ----

    [Fact]
    public async Task A_future_row_is_hidden_on_read_but_survives_an_append()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, Line("2026-10-27T12:00:00Z", "18+") + "\n");
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            Assert.Empty(await log.ReadAllAsync());
            await log.AppendAsync(Entry(Now));
            Assert.Equal(["13-15"], (await log.ReadAllAsync()).Select(e => e.AgeBucket));
            Assert.Contains("2026-10-27T12:00:00Z", File.ReadAllText(path));
            // when the clock catches up the row is visible again
            Assert.Equal(2, (await new JsonFileDecisionLog(path, time: new FixedTime(Now.AddDays(3))).ReadAllAsync()).Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task A_service_clock_more_than_a_day_ahead_of_the_logs_clock_gives_Logged_false()
    {
        var path = TempFile();
        try
        {
            var (provider, _) = Android();
            var log = new JsonFileDecisionLog(path, time: new FixedTime(Now));
            var e = await new AgeSignalsService(On(), provider, log, new FixedTime(Now.AddDays(2))).EvaluateAsync();
            Assert.False(e.Logged);
            Assert.False(File.Exists(path));
            Assert.True((await new AgeSignalsService(On(), provider, log, new FixedTime(Now)).EvaluateAsync()).Logged);
        }
        finally { Cleanup(path); }
    }

    // ---- bounded reader edges ----

    private static string ExtraLine(int targetBytes, bool straddle)
    {
        // A valid line plus an extra key holding "é" (2 bytes); returns a line of exactly targetBytes bytes, or with straddle the 2-byte char crossing byte 512.
        var head = Line()[..^1] + ",\"x\":\"";
        const string tail = "\"}";
        var pad = straddle ? 511 - head.Length : targetBytes - head.Length - 2 - tail.Length;
        return head + new string('a', pad) + "é" + tail;
    }

    [Fact]
    public async Task A_multi_byte_character_across_the_512_byte_cap_drops_the_line_without_error_and_an_exact_512_is_kept()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var exact = ExtraLine(512, straddle: false);
            var crossing = ExtraLine(0, straddle: true);
            Assert.Equal(512, System.Text.Encoding.UTF8.GetByteCount(exact));
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(crossing) > 512);
            File.WriteAllText(path, crossing + "\n" + exact + "\n" + Line(bucket: "18+") + "\n");
            Assert.Equal(["13-15", "18+"], (await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync()).Select(e => e.AgeBucket));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task A_511_byte_line_ending_in_CRLF_is_kept_because_the_CR_counts_as_the_512th_byte()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var ok = Line();
            var line = ok + new string(' ', 511 - ok.Length);
            var tooLong = ok + new string(' ', 512 - ok.Length);   // 512 + CR = 513
            File.WriteAllText(path, line + "\r\n" + tooLong + "\r\n" + Line(bucket: "18+") + "\r\n");
            Assert.Equal(["13-15", "18+"], (await new JsonFileDecisionLog(path, time: new FixedTime(Now)).ReadAllAsync()).Select(e => e.AgeBucket));
        }
        finally { Cleanup(path); }
    }

    // ---- explicit enum numbers ----

    [Fact]
    public void Public_enums_keep_their_numbers()
    {
        Assert.Equal([0, 1, 2], Numbers<AgeSignalsPlatform>());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], Numbers<AgeSignalsState>());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], Numbers<AgeSignalsError>());
        Assert.Equal(10, (int)AgeSignalsError.NoPresenter);
        Assert.Equal(11, (int)AgeSignalsError.Timeout);
        Assert.Equal([0, 1, 2, 3], Numbers<AgeDecision>());
        Assert.Equal([0, 1, 2, 3], Numbers<SignificantChange>());
        Assert.Equal([0, 1, 2], Numbers<SignificantUpdateOutcome>());
        Assert.Equal([0, 1, 2, 3], Numbers<AndroidAccessStatus>());
        Assert.Equal([0, 1, 2, 3], Numbers<AndroidSignificantChange>());
        Assert.Equal(Enumerable.Range(0, 14), Numbers<AndroidErrorKind>());
        Assert.Equal(13, (int)AndroidErrorKind.NoActivity);
        Assert.Equal([0, 1, 2], Numbers<AppleResponseKind>());
        Assert.Equal(Enumerable.Range(0, 8), Numbers<AppleErrorKind>());
    }

    private static IEnumerable<int> Numbers<T>() where T : struct, Enum => Enum.GetValues<T>().Select(v => Convert.ToInt32(v)).Order();

    // ---- helpers ----

    private static string Line(string ts = "2026-10-24T12:00:00Z", string bucket = "13-15") =>
        $$"""{"timestampUtc":"{{ts}}","ageBucket":"{{bucket}}","decision":"Allowed","appVersion":"1.2.3","ruleId":"gate-v1"}""";

    private static DecisionLogEntry Entry(DateTimeOffset at, string bucket = "13-15") => new(at, bucket, AgeDecision.Allowed, "1.2.3", "gate-v1");

    private static void Cleanup(string path) { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }

    // Timers fire only when the test says so.
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var t = new ManualTimer(callback, state);
            lock (_timers) _timers.Add(t);
            return t;
        }
        public void FireAll() { ManualTimer[] all; lock (_timers) all = [.. _timers]; foreach (var t in all) t.Fire(); }
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // Returns a task the test completes itself (completion is synchronous, unlike an async method).
    private sealed class TcsProvider : IAgeSignalsProvider
    {
        public List<TaskCompletionSource<AgeSignalsOutcome>> Sources { get; } = [];
        public bool AutoComplete { get; set; }
        public AgeSignalsPlatform Platform => AgeSignalsPlatform.Android;
        public Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<AgeSignalsOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            Sources.Add(tcs);
            if (AutoComplete) tcs.SetResult(new() { Platform = Platform, State = AgeSignalsState.NotShared });
            return tcs.Task;
        }
        public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken ct = default) => Task.FromResult<bool?>(null);
        public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken ct = default) => Task.FromResult<RegulatoryFeatures?>(null);
        public Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string d, CancellationToken ct = default) => Task.FromResult(SignificantUpdateOutcome.Unsupported);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    // Fake provider that records calls and concurrency, and can wait forever.
    private sealed class ProbeProvider : IAgeSignalsProvider
    {
        private int _running;
        private int _max;
        private int _calls;
        public bool NeverCompletes { get; set; }
        public bool SawCancelableToken { get; private set; }
        public AgeRangeBucket? Bucket { get; set; }
        public bool ThrowOnOtherCalls { get; set; }
        public TimeSpan Delay { get; set; }
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ShowNeverCompletes { get; set; }
        public TaskCompletionSource ShowGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ShowCalls => Volatile.Read(ref _show);
        private int _show;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaxConcurrency => Volatile.Read(ref _max);
        public int Calls => Volatile.Read(ref _calls);
        public AgeSignalsPlatform Platform => AgeSignalsPlatform.Android;

        public async Task<AgeSignalsOutcome> RequestAgeSignalsAsync(AgeSignalsOptions options, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            if (ct.CanBeCanceled) SawCancelableToken = true;   // the service must not hand its tokens to the provider
            var now = Interlocked.Increment(ref _running);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen) { }
            Started.TrySetResult();
            try
            {
                if (NeverCompletes) await Gate.Task;
                else if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
                return Bucket is { } b
                    ? new() { Platform = Platform, State = AgeSignalsState.Shared, Bucket = b }
                    : new() { Platform = Platform, State = AgeSignalsState.NotShared };
            }
            finally { Interlocked.Decrement(ref _running); }
        }

        public Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken ct = default) =>
            ThrowOnOtherCalls ? throw new InvalidOperationException("secret-device-id") : Task.FromResult<bool?>(null);
        public Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken ct = default) =>
            ThrowOnOtherCalls ? throw new InvalidOperationException("secret-device-id") : Task.FromResult<RegulatoryFeatures?>(null);
        public async Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string d, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _show);
            if (ShowNeverCompletes) { await ShowGate.Task; return SignificantUpdateOutcome.Acknowledged; }
            return ThrowOnOtherCalls ? throw new InvalidOperationException("secret-device-id") : SignificantUpdateOutcome.Unsupported;
        }
    }
}
