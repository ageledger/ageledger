# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The first release is a preview.

## Unreleased

### Changed

- The sample app now registers the service through `AddAgeledger`, `AddAgeledgerFileLog` and (Android) `AddAgeledgerActivity` instead of constructing it by hand; the page builds a fresh container whenever its settings change. In Debug builds the Fake backends used by the sample's Fake rows and by the self-test are still built by hand. Nothing in the library changed.
- The Debug self-test has a new row, D1, that resolves the service from the app's own container and checks the saved Enabled setting and the platform of the provider; it also builds a second container through the same registration with Enabled forced on and resolves the service, which on Android proves the activity registration. No device run has recorded it yet.
- The public API baseline files now record the 0.1.0-preview.2 API as shipped.

## 0.1.0-preview.2 (2026-10-30)

### Added

- Dependency-injection helpers (namespace `Microsoft.Extensions.DependencyInjection`): `AddAgeledger(configure)` registers the options and `AgeSignalsService` as singletons, plus the platform provider and backend on Android and iOS; `AddAgeledgerFileLog(path)` registers a `JsonFileDecisionLog` that uses the container's `TimeProvider`, if one is registered (otherwise the system clock); on Android `AddAgeledgerActivity(...)` supplies the foreground activity. On Android, resolving the service with `Enabled = true` and no `AddAgeledgerActivity` throws `InvalidOperationException`. All `TryAdd`, off by default, no log unless asked. Builds with no platform (plain net10.0, Mac Catalyst, Windows) get the new public `NoAgeSignalsProvider`, which reports Unavailable / NotAvailable. Several `AddAgeledger` calls add their `configure` callbacks in call order. New dependency: `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.0 (all three targets).

### Changed

- Copyright and package authorship are now 'The Ageledger Authors' / 'Ageledger'. The published 0.1.0-preview.1 package keeps its original metadata (nuget.org packages cannot be edited).
- The public API baseline files now record the 0.1.0-preview.1 API as shipped.
- The sample's self-test row L4 now seeds 1000 log lines and does one real append, instead of 1001 appends that could exceed its timeout on an older phone. Nothing in the library changed.
- README: `JsonFileDecisionLog` appends flush the whole file to disk each time; the cost per append is now stated.
- `docs/try-the-sample.md`: a guide for running the sample app on your own devices (own app id, Apple sandbox account, Android).
- The sample's app id is one setting (`scripts/sample-id.local`, environment variable `AGELEDGER_SAMPLE_ID` or `-p:AgeledgerSampleId`; default `com.ageledger.sample`). The Android self-test script resolves the launcher activity instead of using a fixed name. Nothing in the library changed.
- README: an "Alternatives" section lists other libraries and samples for the same platform APIs.
- README: NuGet badges; the Unverified list now records the first hosted CI run and release.

### Not verified

- The dependency-injection registrations for Android and iOS (provider, backend, `AddAgeledgerActivity`) are compile-only: they have not run on a device or Simulator.
- The `NoAgeSignalsProvider` path on Mac Catalyst and Windows has not run in a real app.
- The "Unverified" list in `README.md` is the full list.

## 0.1.0-preview.1 (2026-10-27)

First preview release. Everything below ships in `Ageledger` and `Ageledger.Testing`; the public API may still change before 1.0.

### Added

- Package `Ageledger.Testing` (same version, and it depends on exactly that `Ageledger` version, `[version]` in the nuspec): the fakes `FakeAndroidAgeSignalsBackend` and `FakeAppleAgeRangeBackend`, namespace unchanged.
- A public API baseline (`Microsoft.CodeAnalysis.PublicApiAnalyzers`, `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`): an unrecorded public API change fails the build. CI also checks that `Ageledger` contains no `Ageledger.Testing` types and that `Ageledger.Testing` has a README, a `NOTICE` and the right dependency.
- `docs/summarising-the-log.md`: an example (not library API) that groups the decision log into counts by app version, bucket, decision and rule, with a unit test.
- An on-device self-test in Debug builds of the sample (Android and iOS; fakes, except an opt-in `real-play` suite on Android; results as a small JSON file), host scripts `scripts/selftest-android.sh` and `scripts/selftest-ios.sh`, and a per-row coverage table in the device test plan. Nothing in the library changed.
- `SECURITY.md` (private vulnerability reporting) and contribution terms in `CONTRIBUTING.md` (Apache-2.0, DCO sign-off).
- `NOTICE` is packed into the package root.
- One neutral .NET API (`AgeSignalsService`, `AgeSignalsOutcome`, `AgeRangeBucket`, `AgeDecision`) over the age signals of both platforms.
- Android: `GoogleAgeSignalsBackend` over Microsoft's `Xamarin.Google.Android.Play.Age.Signals` 0.0.4 binding (access request, then the age check). The install ID and age-range source are read by the SDK and dropped.
- iOS: a thin Swift wrapper over Apple's DeclaredAgeRange, bound in `Ageledger.IosBinding` and bundled inside the package: `requestAgeRange`, `isEligibleForAgeFeatures`, `requiredRegulatoryFeatures` and `showSignificantUpdateAcknowledgment`, each guarded by OS version.
- Decision log with exactly five fields (`timestampUtc`, `ageBucket`, `decision`, `appVersion`, `ruleId`): `InMemoryDecisionLog` and `JsonFileDecisionLog` (JSON Lines, capped by age and count, `ExportJsonAsync`, `ClearAsync`).
- `AgeBucketLogging` option and the `withheld` bucket value, for apps that want to keep the reported range out of the log.
- The log format is published in `docs/decision-log-format.md` (format version 1), with a unit test that keeps its examples equal to what the library writes.
- The library is off by default: `AgeSignalsOptions.Enabled` is `false`.
- Sample app (`samples/AgeledgerSample`) for on-device checks, and a device test plan with recorded results.
- Fakes for both platforms and unit tests that run on plain `net10.0`.
- Package metadata (description, tags, repository, README, symbol package, deterministic builds), XML documentation for every public member (a missing comment is a build error), and CI checks that the package contains the iOS binding and a root `NOTICE` with the copyright line, and that the sample builds for Android and the iOS Simulator.
- Unit tests for every platform code the library maps (Google's status, significant-change and error codes; the Swift wrapper's response, error, parental-controls, eligibility, regulatory-feature and significant-update codes).

- Cancellation, timeouts and concurrency: an optional `RequestTimeout` (reported as `AgeSignalsError.Timeout`; it also bounds the wait for an earlier request on the same service), one system UI at a time per service, no platform request is cancelled once started, and a fixed rule for what a late answer does.
- File log hardening: bounded line length, future-timestamp handling, a flush before the file is replaced, and cleanup of leftover temporary files. Unusual platform ranges (equal bounds, a bound of 100 or more) are logged as `withheld` instead of failing the write.
- `AgeSignalsError.NoPresenter` for a missing view controller (iOS) or foreground activity (Android) to show the system UI from.
- The Android target references `Xamarin.AndroidX.Lifecycle.LiveData.Core` 2.11.0.1, so an Android app on MAUI 10.0.20 needs no pin of its own to avoid NU1107.
- iOS apps link the Swift framework without a `NativeReference` of their own. The sample adopts the UIScene life cycle; host apps should too (see the README).
- The public surface is small on purpose: `DecisionPolicy`, the log validators, the platform code maps, the Android `Resource` class and the bound Swift-wrapper types are internal. Until 1.0, public interfaces and records may gain members, and `GoogleAgeSignalsBackend`'s constructor exposes the Play binding's `IAgeSignalsManager`, so a breaking binding update is a breaking change here.
- Rule-id examples in the docs, XML comments and the validator message are neutral labels; the log format states the library maps no rule to any law and rule ids should not name an age group.
- Licence Apache-2.0 (NOTICE, package metadata, README).

### Not verified

This is a preview. The "Unverified" list in `README.md` is the full list; in short:

- iPhone: one device, one OS version (iOS 27.0.1), the Apple sandbox account only. No consent sheet was answered or declined and no real (non-sandbox) account was used.
- iOS Simulator: returns Unavailable only, so no real age range was observed there.
- Android: only the sideloaded `NotShared` path of the real Play manager and Google's fakes; no real age signal.
- The release workflow ran once, for this version, and the first hosted CI run passed. Not exercised: the manual "half way" recovery route, the 409 "already exists" wording from nuget.org, and any re-run of the release workflow.
