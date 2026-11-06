# Ageledger

One neutral .NET API over the age signals the two mobile platforms provide, plus a minimal local log of the decisions an app made.

- **Android:** wraps Google Play Age Signals through the `Xamarin.Google.Android.Play.Age.Signals` binding.
- **iOS:** a thin Swift wrapper over Apple's DeclaredAgeRange: `requestAgeRange`, `isEligibleForAgeFeatures`, `requiredRegulatoryFeatures` and `showSignificantUpdateAcknowledgment`.
- **Decision log:** exactly five fields (`timestampUtc`, `ageBucket`, `decision`, `appVersion`, `ruleId`), kept locally. No birthdates and no install, account or device identifiers.
- **Tests:** fakes for both platforms are in the separate `Ageledger.Testing` package, so they stay out of your app.
- **Off by default:** nothing happens until the app sets `AgeSignalsOptions.Enabled = true`.

This package supplies a platform signal and a log. It does not make an app compliant with any law and is not legal advice. The default decision policy is a placeholder; the app owner sets the real one.

## Install

```
dotnet add package Ageledger --prerelease
```

Then register it in a MAUI app (`Enabled` is false until you set it; `AddAgeledger` is in the `Microsoft.Extensions.DependencyInjection` namespace, so it needs no extra using):

```csharp
builder.Services.AddAgeledger(o =>
{
    o.Enabled = true;
    o.AppVersion = "1.4.2";
    o.RuleId = "gate-v1";
    o.RequestTimeout = TimeSpan.FromMinutes(2);   // Apple's sheet and Google's consent screen can wait indefinitely
});
#if ANDROID
builder.Services.AddAgeledgerActivity(() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
#endif
```

Resolve `AgeSignalsService` from the container and `await service.EvaluateAsync()` once a page is showing. The full quick start, the options and the decision each state gives are in the [README](https://github.com/ageledger/ageledger/blob/main/README.md).

- **iOS:** the Declared Age Range capability on the App ID, a profile that grants it, and the entitlement `com.apple.developer.declared-age-range` in an `Entitlements.plist` that the app's `.csproj` points to with `<CodesignEntitlements>` for device builds only (the README shows the property group). An app built with the iOS 27.1 SDK crashed at launch without UIScene adoption on iOS 27.0.1 (observed with the sample; see the README). The package installs on iOS 15.0 and later, but the platform signal exists from iOS 26.0; below that the service reports `Unavailable` / `NotAvailable`.
- **Android:** API 23 or later with the Play Store and Play Services. A sideloaded debug build returned `NotShared` in the project's own run (one phone); real age ranges were not observed. `AddAgeledgerActivity` is required: with the service enabled and no activity registered, creating the service throws `InvalidOperationException`.

For tests, add `Ageledger.Testing` to a test project only, on the same version: `dotnet add package Ageledger.Testing --prerelease`.

## Links

- [Usage, API and requirements](https://github.com/ageledger/ageledger/blob/main/README.md)
- [Decision log format](https://github.com/ageledger/ageledger/blob/main/docs/decision-log-format.md)
- [Retention notes](https://github.com/ageledger/ageledger/blob/main/docs/retention.md)
- [Device test results and what is unverified](https://github.com/ageledger/ageledger/blob/main/docs/device-test-results.md)
- [Changelog](https://github.com/ageledger/ageledger/blob/main/CHANGELOG.md)

Licensed under the Apache License, Version 2.0.
