namespace Ageledger;

/// <summary>
/// Asks the platform for an age signal, applies the app's policy and records the decision.
/// Does nothing unless <see cref="AgeSignalsOptions.Enabled"/> is true.
/// This supplies a platform signal and keeps a log. It does not make an app compliant with any law.
/// </summary>
public sealed class AgeSignalsService
{
    private readonly AgeSignalsOptions options;
    private readonly IAgeSignalsProvider provider;
    private readonly IDecisionLog? log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _flight = new(1, 1);
    // The one system-UI request that may still be running after its caller timed out or cancelled (the sheet or consent screen may still be showing). Guarded by _flight.
    // _pendingEvaluate is the same task when it is an evaluation request (a later EvaluateAsync can join it); null when the pending one is a significant-update request.
    private Task? _pending;
    private Task<AgeSignalsOutcome>? _pendingEvaluate;

    /// <summary>The longest <c>updateDescription</c> accepted by <see cref="ShowSignificantUpdateAcknowledgmentAsync"/>.</summary>
    public const int MaxUpdateDescriptionLength = 1000;

    /// <summary>
    /// The service copies <paramref name="options"/> (including <see cref="AgeSignalsOptions.AgeGates"/>) here: changing the caller's instance afterwards has no effect, so build a new service instead.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Only when <see cref="AgeSignalsOptions.Enabled"/> is true: <see cref="AgeSignalsOptions.AppVersion"/>, <see cref="AgeSignalsOptions.RuleId"/>,
    /// <see cref="AgeSignalsOptions.AgeGates"/>, <see cref="AgeSignalsOptions.AgeBucketLogging"/>, <see cref="AgeSignalsOptions.MinimumAge"/>,
    /// <see cref="AgeSignalsOptions.AdultAge"/> (each 0 to 99, adult age not below minimum age), <see cref="AgeSignalsOptions.NoSignalDecision"/>,
    /// <see cref="AgeSignalsOptions.VerificationRequiredDecision"/> or <see cref="AgeSignalsOptions.RequestTimeout"/> (more than zero, at most 10 minutes)
    /// has a value the decision log or the platform cannot accept.
    /// Checked here so a bad setting fails at start-up, not after a platform call. A disabled service never reads these values.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="provider"/> is null.</exception>
    public AgeSignalsService(AgeSignalsOptions options, IAgeSignalsProvider provider, IDecisionLog? log = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);
        var snapshot = options.Snapshot();
        if (snapshot.Enabled)
        {
            DecisionLogEntry.ValidateAppVersion(snapshot.AppVersion);
            DecisionLogEntry.ValidateRuleId(snapshot.RuleId);
            if (!AgeSignalsOptions.AreValidAgeGates(snapshot.AgeGates)) throw new ArgumentException(AgeSignalsOptions.AgeGatesRule, nameof(options));
            if (snapshot.MinimumAge is < 0 or > 99) throw new ArgumentException("Minimum age must be 0 to 99.", nameof(options));
            if (snapshot.AdultAge is < 0 or > 99) throw new ArgumentException("Adult age must be 0 to 99.", nameof(options));
            if (snapshot.AdultAge < snapshot.MinimumAge) throw new ArgumentException("Adult age must not be below the minimum age.", nameof(options));
            if (!Enum.IsDefined(snapshot.AgeBucketLogging)) throw new ArgumentException("Age bucket logging is not a defined value.", nameof(options));
            if (!Enum.IsDefined(snapshot.NoSignalDecision)) throw new ArgumentException("No-signal decision is not a defined value.", nameof(options));
            if (!Enum.IsDefined(snapshot.VerificationRequiredDecision)) throw new ArgumentException("Verification-required decision is not a defined value.", nameof(options));
            if (snapshot.RequestTimeout is { } t && (t <= TimeSpan.Zero || t > AgeSignalsOptions.MaxRequestTimeout))
                throw new ArgumentException("Request timeout must be more than zero and at most 10 minutes.", nameof(options));
        }
        this.options = snapshot;
        this.provider = provider;
        this.log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True when <see cref="AgeSignalsOptions.Enabled"/> was true when the service was created.</summary>
    public bool IsEnabled => options.Enabled;

    /// <summary>
    /// Asks the platform, applies the policy and logs the decision.
    /// One system UI at a time per service: a second call waits for the first, and can cancel while waiting.
    /// After a timeout or a cancel the system UI may still be showing; the library never cancels a platform request it has started, so the next call of either kind
    /// (this one or <see cref="ShowSignificantUpdateAcknowledgmentAsync"/>) waits for it. A repeated <c>EvaluateAsync</c> that is holding the service (it has joined that request) when the person answers gets the answer; a call still queued behind another call, or behind a significant-update call, does not join, so an evaluation answer that arrives then is discarded. An answer that arrives while no call has joined is discarded (not logged) and the next call asks again, so the consent screen can appear again.
    /// One sheet can therefore produce two log rows: a Timeout row for the first call, then the answer row for the call that joined it.
    /// A request that never completes makes later calls end in Timeout, or throw on the caller's cancel (or wait without limit if neither a token nor a timeout is set), until the app builds a new service.
    /// Do not call this from <see cref="AgeSignalsOptions.SharedRangePolicy"/> or a custom <see cref="IDecisionLog"/> on the same service (not supported: it would recurse without end), and never block on it from a UI thread (<c>.Result</c>, <c>.Wait()</c>).
    /// A token that is already cancelled throws before anything else happens. If <see cref="AgeSignalsOptions.RequestTimeout"/> expires (it also bounds the wait for an earlier request) the result is an
    /// <see cref="AgeSignalsState.Error"/> with <see cref="AgeSignalsError.Timeout"/> and the decision is logged; the caller's own cancellation throws <see cref="OperationCanceledException"/>.
    /// Apple's sheet and Google Play's consent screen can wait for the person indefinitely, so pass a token with a timeout or set the option.
    /// <see cref="AgeSignalsOptions.SharedRangePolicy"/> can run for different calls at the same time, so it must be thread-safe and free of side effects.
    /// An exception from <see cref="AgeSignalsOptions.SharedRangePolicy"/> propagates; so does <see cref="InvalidOperationException"/> when it returns an undefined decision.
    /// </summary>
    /// <param name="cancellationToken">Stops this call's wait (the platform request itself is not cancelled).</param>
    /// <returns>The platform result, the app's decision and whether the decision was logged.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InvalidOperationException"><see cref="AgeSignalsOptions.SharedRangePolicy"/> returned a value that is not a defined <see cref="AgeDecision"/>.</exception>
    public async Task<AgeSignalsEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled) return new(AgeSignalsOutcome.Disabled, AgeDecision.NotApplicable);

        AgeSignalsOutcome result;
        // The timeout starts before the wait for the lock, so it bounds the whole call.
        using var timeout = options.RequestTimeout is { } limit ? new CancellationTokenSource(limit, _time) : null;
        using var linked = timeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked?.Token ?? cancellationToken;
        var acquired = false;
        try
        {
            await _flight.WaitAsync(token).ConfigureAwait(false);
            acquired = true;
            Task<AgeSignalsOutcome> work;
            if (_pendingEvaluate is { IsCompleted: false } running) work = running;   // join the evaluation request that is still up
            else
            {
                if (_pending is { IsCompleted: false } other) await WaitForEnd(other, token).ConfigureAwait(false);   // a significant-update UI is up: wait, then ask
                token.ThrowIfCancellationRequested();   // the caller's token or the timeout: a timeout that fired during the wait must not start a new UI
                work = StartProvider();
                _pending = work;
                _pendingEvaluate = work;
            }
            // WaitAsync also stops the wait for a provider that ignores its token.
            result = await work.WaitAsync(token).ConfigureAwait(false);
            if (ReferenceEquals(_pending, work)) { _pending = null; _pendingEvaluate = null; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            result = new() { Platform = provider.Platform, State = AgeSignalsState.Error, Error = AgeSignalsError.Timeout };
        }
        catch (Exception)
        {
            // Never let a platform failure crash the app, and never put the exception text (it can hold identifiers) anywhere.
            // A cancellation that is neither the caller's nor the timeout's lands here too.
            result = new() { Platform = provider.Platform, State = AgeSignalsState.Error, Error = AgeSignalsError.Other };
        }
        finally { if (acquired) _flight.Release(); }

        var decision = DecisionPolicy.Decide(result, options);
        var logged = false;
        if (log is not null)
        {
            try
            {
                // The write is short and bounded, so it is not cancelled: a finished evaluation is never discarded because the token fired after the platform call.
                await log.AppendAsync(DecisionLogEntry.From(result, decision, options, _time.GetUtcNow()), CancellationToken.None).ConfigureAwait(false);
                logged = true;
            }
            catch (Exception)
            {
                // A log failure (disk full, ...) must not lose the evaluation; Logged tells the caller. No exception text is kept.
            }
        }
        return new(result, decision, logged);
    }

    // Waits until the task has ended, however it ended; only this call's token can stop the wait.
    private static Task WaitForEnd(Task task, CancellationToken token) =>
        task.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).WaitAsync(token);

    // The provider gets no token: the caller's token and the timeout only stop this call's wait (WaitAsync). Cancelling the provider could drop a request
    // whose system UI is still showing and invite a second one; the request stays in the pending slot and the next call waits for it.
    private Task<AgeSignalsOutcome> StartProvider()
    {
        var task = provider.RequestAgeSignalsAsync(options, CancellationToken.None);
        task.ObserveFaults();   // a fault after this call gave up waiting must not surface as an unobserved exception
        return task;
    }

    /// <summary>Null when disabled, when the platform cannot say, or when the platform call fails (no exception text is kept). A cancelled token throws.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<bool?> IsEligibleForAgeFeaturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled) return null;
        try { return await provider.IsEligibleForAgeFeaturesAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>Null when disabled, when the platform cannot say, or when the platform call fails (no exception text is kept). A cancelled token throws.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<RegulatoryFeatures?> GetRequiredRegulatoryFeaturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled) return null;
        try { return await provider.GetRequiredRegulatoryFeaturesAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>Shows the system significant-change acknowledgment (iOS 26.4+).</summary>
    /// <param name="updateDescription">Text shown by the system; not null, empty or white space, at most <see cref="MaxUpdateDescriptionLength"/> characters.</param>
    /// <remarks>Shares the lock and the one pending system UI with <see cref="EvaluateAsync"/>: it waits for an earlier evaluation request or significant-update request that is still unfinished (even after its caller timed out or cancelled), then starts its own. A cancelled call stops only its wait; the sheet stays tracked. See <see cref="EvaluateAsync"/> for the full rule.</remarks>
    /// <returns><see cref="SignificantUpdateOutcome.Unsupported"/> when disabled; <see cref="SignificantUpdateOutcome.Failed"/> when the platform call fails (no exception text is kept).</returns>
    /// <param name="cancellationToken">Stops this call's wait (the system UI itself stays).</param>
    /// <exception cref="ArgumentException"><paramref name="updateDescription"/> is null, blank or too long.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<SignificantUpdateOutcome> ShowSignificantUpdateAcknowledgmentAsync(string updateDescription, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateDescription);
        if (updateDescription.Length > MaxUpdateDescriptionLength)
            throw new ArgumentException($"Update description must be at most {MaxUpdateDescriptionLength} characters.", nameof(updateDescription));
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled) return SignificantUpdateOutcome.Unsupported;
        // Same lock and pending slot as EvaluateAsync: one system UI at a time per service. The backend gets no token (a cancelled call stops only its wait;
        // the sheet stays tracked in the slot), and this call has no timeout of its own.
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pending is { IsCompleted: false } other) await WaitForEnd(other, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();   // after the wait, so a cancel that fired during it does not start a new UI
            var task = provider.ShowSignificantUpdateAcknowledgmentAsync(updateDescription, CancellationToken.None);
            task.ObserveFaults();
            _pending = task;
            _pendingEvaluate = null;
            var outcome = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(_pending, task)) _pending = null;
            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return SignificantUpdateOutcome.Failed; }
        finally { _flight.Release(); }
    }
}
