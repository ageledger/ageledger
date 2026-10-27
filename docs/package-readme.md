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

For tests, add `Ageledger.Testing` to a test project only, on the same version: `dotnet add package Ageledger.Testing --prerelease`.

## Links

- [Usage, API and requirements](https://github.com/ageledger/ageledger/blob/main/README.md)
- [Decision log format](https://github.com/ageledger/ageledger/blob/main/docs/decision-log-format.md)
- [Retention notes](https://github.com/ageledger/ageledger/blob/main/docs/retention.md)
- [Device test results and what is unverified](https://github.com/ageledger/ageledger/blob/main/docs/device-test-results.md)
- [Changelog](https://github.com/ageledger/ageledger/blob/main/CHANGELOG.md)

Licensed under the Apache License, Version 2.0.
