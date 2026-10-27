# Ageledger.Testing

Test doubles for [Ageledger](https://www.nuget.org/packages/Ageledger): `FakeAndroidAgeSignalsBackend` and `FakeAppleAgeRangeBackend`. They stand in for the platform backends so an app's unit tests can run on plain .NET (no device, emulator or platform SDK), for example:

```csharp
using Ageledger;
using Ageledger.Testing;

var options = new AgeSignalsOptions { Enabled = true, AppVersion = "1.0.0", RuleId = "gate-v1" };
var fake = new FakeAppleAgeRangeBackend { NextResponse = new AppleAgeRangeResponse(AppleResponseKind.Sharing, 13, 15) };
var service = new AgeSignalsService(options, new AppleAgeSignalsProvider(fake));

AgeSignalsEvaluation e = await service.EvaluateAsync();
// e.Result.State is AgeSignalsState.Shared, e.Result.Bucket.Label is "13-15",
// and with the placeholder policy e.Decision is AgeDecision.AllowedWithParentalConsent.
```

Add it to a test project only (`dotnet add package Ageledger.Testing --prerelease`); do not ship it in an app. It depends on exactly the same version of the Ageledger package (`[version]` in its nuspec), so keep the two packages on the same version. `AgeSignalsService` has no interface, so to stand in for the platform you use these fake backends rather than mocking the service. The fakes use made-up data and never send anything anywhere.

## Links

- [Usage, API and requirements](https://github.com/ageledger/ageledger/blob/main/README.md)
- [Changelog](https://github.com/ageledger/ageledger/blob/main/CHANGELOG.md)

Licensed under the Apache License, Version 2.0.
