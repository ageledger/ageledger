# Ageledger

> Licensed under the Apache License 2.0. Source and issues: <https://github.com/ageledger/ageledger>. Version 0.1.0-preview.1 is a preview: the API can still change.

A small .NET 10 library for Android and iOS apps (.NET MAUI first) that gives an app **one neutral API** for the age signals the two platforms
provide, and keeps a **minimal local log of the decisions the app made**.

- Android: wraps Microsoft's existing binding `Xamarin.Google.Android.Play.Age.Signals` 0.0.4 (Google Play Age Signals API). Nothing is rebound.
- iOS: a thin Swift wrapper (`native/ios`, built into an xcframework and bound in `src/Ageledger.IosBinding`) over Apple's `DeclaredAgeRange` framework. It covers `requestAgeRange`, `isEligibleForAgeFeatures`, `requiredRegulatoryFeatures` and `showSignificantUpdateAcknowledgment`, plus the decision log below.

## Install

```
dotnet add package Ageledger --prerelease
```

(or `--version 0.1.0-preview.1` to pin it). Add it to the app project. For tests, add the fakes to a test project only, on the same version:

```
dotnet add package Ageledger.Testing --prerelease
```

Keep `Ageledger` and `Ageledger.Testing` on the same version: `Ageledger.Testing` depends on exactly the `Ageledger` version it was built with.

## What this is not

- **It does not make an app compliant with any law.** It supplies the platform's age signal and records what your app decided. Apple and Google both leave the compliance duty with the developer.
- **It is not legal advice.** The law list below is background, not a reading of what binds your app.
- The default decision policy is a placeholder. The app owner must set the real one (`SharedRangePolicy`, `NoSignalDecision`, `VerificationRequiredDecision`, `MinimumAge`, `AdultAge`).

## Off by default

`AgeSignalsOptions.Enabled` is `false`. While it is false the service makes no platform call, shows no system sheet, writes no log and returns `AgeSignalsState.Disabled` / `AgeDecision.NotApplicable`. An app that references the library changes nothing until it opts in.

```csharp
var options = new AgeSignalsOptions
{
    Enabled = true,                 // opt in
    AgeGates = [13, 16, 18],        // Apple gates (1-3 ascending ages, first at least 2, each 2 or more above the last). Google's bands are set in Play Console.
    AppVersion = "1.4.2",           // "1.4.2" or "1.4.2-beta1"; no build suffix such as "(57)"
    RuleId = "gate-v1",             // your own label for the rule in force; never a person identifier or an age group
};
// With Enabled = true, new AgeSignalsService(...) throws ArgumentException straight away if AppVersion, RuleId, AgeGates,
// AgeBucketLogging, NoSignalDecision, VerificationRequiredDecision, MinimumAge, AdultAge or RequestTimeout is not acceptable (MinimumAge and
// AdultAge: each 0 to 99, AdultAge not below MinimumAge; gates: one to three ascending ages, 2 to 99, each at least 2 above the one before, so
// every range label such as "0-12" or "13-15" is a valid log bucket; RequestTimeout: null, or more than zero and at most 10 minutes).
// With Enabled = false nothing is validated, so an app that has not opted in cannot fail at start-up over these values.
// The service copies the options (and the AgeGates array) when it is created: changes to your AgeSignalsOptions instance afterwards are
// ignored, so build a new service to change a setting. The service has no Options property.

#if ANDROID
IAgeSignalsProvider provider = new AndroidAgeSignalsProvider(
    new GoogleAgeSignalsBackend(() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity));
#elif IOS
IAgeSignalsProvider provider = new AppleAgeSignalsProvider(new AppleAgeRangeBackend());
#endif

var log = new JsonFileDecisionLog(Path.Combine(FileSystem.AppDataDirectory, "age-decisions.jsonl"));
var service = new AgeSignalsService(options, provider, log);

AgeSignalsEvaluation e = await service.EvaluateAsync(cancellationToken);
// e.Result.State, e.Result.Bucket (e.g. "13-15"), e.Decision (Allowed / AllowedWithParentalConsent / Blocked / NotApplicable),
// e.Logged (true only when a log is set and the entry was written; a log failure never throws from EvaluateAsync)
```

**Timeouts and cancellation.** Apple's sheet and Google Play's consent screen can wait for the person indefinitely, so pass a token with a timeout or set `AgeSignalsOptions.RequestTimeout` (default none; up to 10 minutes). When `RequestTimeout` expires, `EvaluateAsync` returns `Error` / `AgeSignalsError.Timeout` and logs the decision as usual (the system UI itself cannot be dismissed from here and stays until the person closes it). When the caller's token is cancelled, `EvaluateAsync` throws `OperationCanceledException` and logs nothing; a token that is already cancelled throws before any platform call (the same for the three methods below). The timeout also bounds the wait for an earlier request on the same service; a timeout while waiting is reported the same way (`Error` / `Timeout`, logged). The rule is **one system UI at a time per service**, shared by `EvaluateAsync` and `ShowSignificantUpdateAcknowledgmentAsync`:
- After a timeout or a cancel the sheet or consent screen may still be showing. The library never cancels a platform request it has started; the token and the timeout only stop that call's wait.
- The next call of either kind waits for that request. A repeated `EvaluateAsync` joins the same request and gets the person's answer only if it has joined (is holding the service) when they answer; a call still queued behind another call, or behind a significant-update call, does not join, so an evaluation answer that arrives then is discarded.
- An answer that arrives while no call has joined is discarded (not logged), and the next call asks again, so the consent screen can appear again.
- One sheet can produce two log rows: a `Timeout` row for the first call, then the answer row for the call that joined it.
- A request that never completes makes later calls end in `Timeout`, or throw on the caller's cancel (or wait without limit if neither a token nor a timeout is set) until the app builds a new service.

`SharedRangePolicy` can run for different calls at the same time, so it must be thread-safe and free of side effects. Do not call `EvaluateAsync` from `SharedRangePolicy` or from a custom `IDecisionLog` on the same service (not supported: it would recurse without end), and never block on these methods from a UI thread (`.Result`, `.Wait()`); `await` them. A cancellation that is neither the caller's nor the timeout's is reported as `Error` / `Other`.

**Your own policy code.** An exception thrown by `SharedRangePolicy` propagates out of `EvaluateAsync` (nothing is logged); if it returns a value that is not a defined `AgeDecision`, `EvaluateAsync` throws `InvalidOperationException`.

iOS only, guarded by OS version inside the library (returns `null` / `Unsupported` on older iOS):

```csharp
bool? eligible = await service.IsEligibleForAgeFeaturesAsync();                  // iOS 26.2+
RegulatoryFeatures? needed = await service.GetRequiredRegulatoryFeaturesAsync(); // iOS 26.4+
await service.ShowSignificantUpdateAcknowledgmentAsync("What changed");          // iOS 26.4+
```

The update description must not be null, empty or white space and is at most 1000 characters (`ArgumentException` otherwise). A platform failure in these three methods gives `null` or `SignificantUpdateOutcome.Failed`, never an exception or exception text.

**Testing your own code.** The fakes (`FakeAndroidAgeSignalsBackend`, `FakeAppleAgeRangeBackend`, namespace `Ageledger.Testing`) are in a separate package, `Ageledger.Testing`, so they are not part of your app. Add it to a test project only (`dotnet add package Ageledger.Testing --prerelease`, on the same version as `Ageledger`); it depends on exactly the same version of the `Ageledger` package (`[version]` in its nuspec) and runs on plain .NET with no device. `AgeSignalsService` has no interface, so to stand in for the platform you use the fake backends rather than mocking the service:

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

**Public API.** The public surface is recorded in `PublicAPI.Unshipped.txt` files next to each project (platform-specific types in `PublicAPI/android` and `PublicAPI/ios`); the build fails if a public member is added, removed or has its signature changed without updating them (a `CoreOnly` build checks only the `net10.0` file; see CONTRIBUTING for the per-target note). The backends, providers, `IDecisionLog` implementations and the neutral model are public; the code maps, the decision policy function and the field validators are internal. Until 1.0, public interfaces and records may gain members; keep `Ageledger` and `Ageledger.Testing` on the same version. `GoogleAgeSignalsBackend`'s constructor exposes the Play binding's `IAgeSignalsManager`, so a breaking update of that binding is a breaking change here. The iOS binding assembly (`Ageledger.IosBinding`, bundled in the package) is an implementation detail and not for direct use: its bound types are internal and only `Ageledger` can see them.

## Result model

| | |
|---|---|
| `AgeSignalsState` | `Disabled`, `Shared`, `Declined` (Apple), `NotShared` (Google), `VerificationRequired` (Google), `Unavailable`, `Error` |
| `AgeRangeBucket` | `Lower` / `Upper` bounds and a label such as `0-12`, `13-15`, `18+`, `unknown` |
| `AgeSignalsPlatform` | `Android`, `iOS`, `None` (the `Disabled` result, where no platform was asked) |
| `AgeDecision` | `NotApplicable`, `Allowed`, `AllowedWithParentalConsent`, `Blocked` |
| extras | `SignificantChange` (Google), `ParentalControls` (Apple), `AgeSignalsError` category (includes `NoPresenter`: on iOS no view controller or foreground window scene to show Apple's sheet from; on Android no usable foreground activity (none, finishing or destroyed) for Play's consent screen; and `Timeout`: `RequestTimeout` expired). Enum numbers are fixed; new members are appended |
| `AgeSignalsEvaluation` | `Result`, `Decision`, `Logged` |

Default decision policy (placeholder; the app owner sets the real one): it uses the **younger end** of the reported range. The lower bound (`null` counts as 0) decides: below `MinimumAge` (13) is `Blocked`, below `AdultAge` (18) is `AllowedWithParentalConsent`, otherwise `Allowed`. So `0-17` (a null lower bound counts as 0) and `12-17` are `Blocked` and `16-17` asks for parental consent. The upper bound is not used. `SharedRangePolicy` replaces all of this.

The result type has **no** install ID, age-source tier, declaration type, account or device identifier: the platform backends read them from the SDKs and drop them, so the app never receives (and cannot log) them.

## Decision log (data minimisation)

Each entry has exactly five fields: `timestampUtc`, `ageBucket`, `decision`, `appVersion`, `ruleId`. Never a birthdate, install ID, account ID, source tier or device identifier.

- Values are constrained to fixed shapes, which reduces but does not prevent misuse (a value that fits the shape can still carry something unintended). The bucket is `unknown`, `withheld`, `18+` or `13-15` with the lower number below the upper (no exact ages like `7-7`); the version is up to four numeric parts with an optional `alpha`/`beta`/`rc`/`preview`/`dev` tag (`1.4.2`, `1.4.2-beta1`; no free-text or build suffix); the rule id starts with a letter, is at most 48 characters and cannot contain a date shape (`2010-05-01`, `2010.05.01`), 8 or more digits, or a run of 8 or more hex characters (GUID or hash fragments). Known limitation: a calendar-style version whose first part is a year (`2026.10.1`) is accepted, so a date written as a version would pass.
- `DecisionLogEntry.FromFields` refuses any other key (`birthdate`, `installId`, `accountId`, `sourceTier`, `deviceId`, ...) and requires the decision to be exactly one of the names `NotApplicable`, `Allowed`, `AllowedWithParentalConsent`, `Blocked`.
- JSON form (one object per line in the file, one array in `ExportJsonAsync()`): the five keys as strings. `timestampUtc` is written as UTC ISO 8601 in whole seconds with a trailing `Z` (`2026-10-24T12:00:00Z`; a fraction is truncated, which is less linkable); on read, only an explicit `Z` or `+00:00` is accepted (a timestamp with no offset, or any other offset, is rejected). `decision` is a PascalCase name; integers, other casing and padded or comma-separated text are rejected. The file uses `\n` line ends and UTF-8 without a byte-order mark. The full format, written so other languages can produce the same log, is in [docs/decision-log-format.md](docs/decision-log-format.md); a unit test keeps its example equal to what this library writes.
- To turn the log into counts for your own overview (for example per app version, bucket and decision), see [docs/summarising-the-log.md](docs/summarising-the-log.md). It is an example, not library API.
- `JsonFileDecisionLog`: local JSON Lines file, capped by count (default 1000, allowed 1 to 100000) and age (default 365 days, allowed 1 to 3650 days; both limits apply when appending and when reading; out-of-range values throw `ArgumentOutOfRangeException`), `ExportJsonAsync()` returns one JSON array, `ClearAsync()` deletes it and any leftover `<path>.<32 hex>.tmp` files from an interrupted append. A row past the limits is hidden on read but stays in the file until the next append or `ClearAsync()`. `InMemoryDecisionLog` has no age or count limits. Nothing is sent anywhere. Several instances in one process on the same path share one lock and a unique temporary file; separate processes are not coordinated.
- The file log uses its own `TimeProvider`. If the service's clock is more than a day ahead of the log's, appends fail and `Logged` is `false`; pass the same `TimeProvider` to both.
- Appending a null entry throws `ArgumentNullException` in both logs. `JsonFileDecisionLog` refuses an entry whose timestamp is more than a day ahead of its own clock, hides such rows on read (they stay in the file until their time comes, an append does not delete them), drops any line longer than 512 bytes (not counting the final `\n`; a `\r` before it counts; the line is never loaded whole), and flushes the temporary file to disk before it replaces the log.
- On read, a line is dropped if it is not valid JSON, repeats a key, misses any of the five keys, has a default timestamp, or has a value that fails the checks above. A line that is valid but has extra keys is kept without those keys. A hand-edited file therefore cannot add other fields.
- If the log cannot be written (for example storage errors), `EvaluateAsync` still returns the result and decision with `Logged = false`; the exception text is not kept. The log write is not cancelled by the caller's token: a token that fires after the platform call has returned does not discard the finished evaluation.
- Digits in bucket and version values must be ASCII `0-9`; other Unicode digits are rejected.
- `AgeSignalsOptions.AgeBucketLogging` chooses what the log stores in `ageBucket`: `Exact` (default; the reported range such as `13-15`) or `Withheld` (the literal `withheld` is written when a range was received, `unknown` when none was, so a reader can tell redaction from no signal; with either setting a received range that cannot be written, such as `7-7`, is also `withheld`). The library does not choose by country or state; the app does. With `Withheld` the explicit range is not stored, but the `decision` value can still imply an age group (with the default policy, `Blocked` means a lower bound under 13). Rows already written are not changed; `ClearAsync()` deletes the whole log. If you must keep nothing derived from the age signal, pass no log to the service. Google's and Apple's developer terms limit what the age signal may be used for and bar passing it to third parties (Google, except as strictly required by law) or to data brokers and resellers (Apple), so do not send exported log rows to analytics, advertising or other outside services. Some US app-store laws limit what a developer may do with, or require deleting, age category data supplied by a store; the texts and the points that remain open are in `docs/retention.md`. An app that keeps the log should state in its privacy policy what it keeps and the `retention` and `maxEntries` settings it uses. Not legal advice; using this setting does not make an app compliant with any law.
- A platform exception is turned into an `Error` result; its text is never stored.

## Layout

```
src/Ageledger             library: net10.0 (model, mapping, policy, log), net10.0-android, net10.0-ios
src/Ageledger.Testing     fakes for both platforms (package Ageledger.Testing), plain net10.0
src/Ageledger.IosBinding  net10.0-ios binding of the Swift wrapper (ApiDefinition.cs + the xcframework)
native/ios                Swift wrapper source + build.sh (builds AgeledgerNative.xcframework, device + simulator arm64)
tests/Ageledger.Tests     xUnit on plain net10.0, using Ageledger.Testing
samples/AgeledgerSample   one-page MAUI app for device checks; Debug builds hold an on-device self-test
scripts                   host scripts that run that self-test on an Android device or an iPhone (docs/device-test-plan.md, section 7.6), and `ios-uitests.sh`, which runs the iPhone UI test in tests/ios-device-uitests (section 7.7; needs at least one Touch ID touch from the person holding the phone, and sets the sandbox scenario back afterwards)
```

`dotnet build` of the iOS binding runs `native/ios/build.sh` (needs Xcode) when the xcframework is missing or a Swift source or `build.sh` is newer than it. The xcframework is a build output and is not committed. The xcframework carries a dSYM per slice (device and simulator; line tables only; the wrapper's source path is relative and the SDK root is written as `/SDK`) so that crashes inside the wrapper show function names and, where available, source lines; together they add about 25 KB to the package (measured 2026-10-27). They are inside `lib/net10.0-ios*/Ageledger.IosBinding.resources.zip`, under `AgeledgerNative.xcframework/<slice>/dSYMs/`, so you can point `atos` or Xcode at them by hand. Read from the targets file of the 26.5 .NET iOS workload (workload version 10.0.300.3; `Xamarin.Shared.Sdk.targets` in the `Microsoft.iOS.Sdk.net10.0_26.5` pack; not run with this package): it looks for `<framework>.dSYM` next to the `.framework` folder, does not read the xcframework's `dSYMs/` folder, and generates its own `AgeledgerNative.framework.dSYM`, which is expected to have no debug information (symbol table only). Consider replacing that generated dSYM with the shipped one before uploading symbols. dotnet/macios PR #25979 (merged into main 2026-07-23, checked 2026-10-27) copies xcframework dSYMs; which workload release ships it is not known.

Requirements for an app:

- **Android:** API 23+ and Play Store/Play Services.
  - The library's Android target references `Xamarin.AndroidX.Lifecycle.LiveData.Core` 2.11.0.1, so an Android app on MAUI 10.0.20 needs no manual pin to avoid NU1107 (seen when an app referenced both MAUI 10.0.20 and the age-signals binding). That reference pins `Xamarin.AndroidX.Lifecycle.Common` to [2.11.0.1, 2.11.1); an app that needs a newer version can add its own higher reference.
  - Restoring an Android app on MAUI 10.0.20 prints NU1608 warnings (Lifecycle Ktx, ViewModel and SavedState packages outside their dependency constraints). They appear because the AndroidX Lifecycle packages are raised above the 2.9.x range that MAUI's own packages declare; an app that does not want them can set `<NoWarn>NU1608</NoWarn>`.
- **iOS:** iOS 15.0+ to install; below iOS 26 the service reports Unavailable/Unsupported (the platform API starts at iOS 26.0, and the regulatory calls at 26.2 and 26.4).
  - Building the Swift wrapper needs the iOS 26.4 SDK or newer: `native/ios/build.sh` and the Swift source fail the build otherwise, instead of producing a framework that always reports not available.
  - The app needs the **Declared Age Range** capability, i.e. the entitlement `com.apple.developer.declared-age-range` in `Entitlements.plist`:

```xml
<key>com.apple.developer.declared-age-range</key>
<true/>
```

Apple's technote [TN3187](https://developer.apple.com/documentation/technotes/tn3187-migrating-to-the-uikit-scene-based-life-cycle) (checked 2026-10-25) says that in the next major release after iOS 26 the UIScene life cycle is required when building with the latest SDK, otherwise the app won't launch. The host iOS app should therefore adopt scenes (`UIApplicationSceneManifest` in `Info.plist` and a scene delegate; in MAUI, a subclass of `MauiUISceneDelegate`, see Microsoft's [MAUI Window page](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/window?view=net-maui-10.0)). Observed here: the sample app, built with the iOS 27.1 SDK, crashed at launch on an iPhone running iOS 27.0.1 until it adopted scenes (`samples/AgeledgerSample/Platforms/iOS`).

## Sample app

`samples/AgeledgerSample` is a one-page MAUI app (`net10.0-android`, `net10.0-ios`) for the device test plan in `docs/device-test-plan.md`. It is not part of `Ageledger.slnx`, is not packed, and makes no network calls itself (the platform services it calls may, and the opt-in `real-play` suite of its self-test calls Google's live service). It shows only the library's result and the five-field log. Debug builds also contain a self-test that runs many plan rows on a device by itself, driven by the scripts in `scripts/` (see section 7.6 of the plan). Debug builds add test controls (Google's `FakeAgeSignalsManager` on Android, invalid settings, log fault injection); Release builds use the real platform manager only.

```
dotnet build samples/AgeledgerSample -f net10.0-android -c Debug
dotnet build samples/AgeledgerSample -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64
```


The sample sets `ValidateXcodeVersion` to false because the .NET for iOS pack (26.5) expects Xcode 26.5 while the sample was built with Xcode 27.1 (checked 2026-10-25); remove it once they match. The app has no `NativeReference` of its own: the Swift framework reaches it through the library. The Simulator needs no signing, but the build still looks for a signing identity unless `-p:EnableCodeSigning=false` is passed (needed on a Mac without one). An iPhone build (`-p:RuntimeIdentifier=ios-arm64`) is signed manually with a provisioning profile that grants `com.apple.developer.declared-age-range`; `CodesignKey` and `CodesignProvision` come from a local, untracked `samples/AgeledgerSample/AgeledgerSample.csproj.user`, and `Entitlements.plist` is applied to device builds only. Nothing account-specific is committed. It has run on an Android phone, on an iPhone (iOS 27.0.1, Apple sandbox account, UI test) and, headless, on the iOS Simulator (see [docs/device-test-results.md](docs/device-test-results.md)).

## APIs covered (checked 2026-10-25 against the code, the iPhoneOS 27.1 SDK in Xcode 27.1 and the Android binding 0.0.4)

Android (Microsoft binding 0.0.4, `net10.0-android36.0`): `AgeSignalsManagerFactory.Create`, `IAgeSignalsManager.RequestAgeSignalsAccess` (with `AgeSignalsAccessRequest` built from the foreground `Activity`) then `CheckAgeSignals`, `AgeSignalsAccessResult.AgeSignalsStatus`, `AgeSignalsResult.AgeLower/AgeUpper/SignificantChangeStatus`, `AgeSignalsException.ErrorCode` and the `AgeSignalsErrorCode` constants. `InstallId` and `AgeRangeSource` are deliberately not read into the model. Google's `FakeAgeSignalsManager` can be passed to `GoogleAgeSignalsBackend` for on-device tests.

iOS (swiftinterface in the iPhoneOS 27.1 SDK, Xcode 27.1): `AgeRangeService.shared.requestAgeRange(ageGates:_:_:in:)` (iOS 26.0), `Response`, `AgeRange.lowerBound/upperBound/activeParentalControls`, `AgeRangeService.Error`, `isEligibleForAgeFeatures` (26.2), `requiredRegulatoryFeatures` and `RegulatoryFeature` (26.4), `showSignificantUpdateAcknowledgment(in:updateDescription:)` (26.4, iOS only). Not bound: `AgeRangeDeclaration` (dropped on purpose), PermissionKit `SignificantAppUpdateTopic`, the SwiftUI environment actions.

## Laws it was designed around (background only; not legal advice)

From desk research checked 2026-10-25 (primary sources are the enrolled bills; details and caveats in `docs/retention.md`):

| Law | Operative date (per the source read) | Note |
|---|---|---|
| Texas SB 2420 (App Store Accountability Act) | Text: takes effect 2026-01-01 (Sec. 3). Court status not checked from primary sources; see docs/retention.md | Developer duties in Bus. & Com. Code ch. 121 |
| California AB 1043 (Digital Age Assurance Act) | 2027-01-01 | Developers request an age-bracket signal; read from code-section mirrors, not the official chaptered text |
| Alabama HB 161 | 2027-01-01 | Enrolled text read |
| Utah ch. 13-76 as amended by H.B. 498 | 2027-05-06 | Private right of action only |
| Louisiana Act 185 (HB 977 of 2026) | 2027-07-01 | Replaces Act 481, which never took effect |

Retention of age-category data, and what the log setting above does and does not cover: `docs/retention.md`.

## Verified on devices

Details and the rows not run are in [docs/device-test-results.md](docs/device-test-results.md). On an Android phone (Debug sample app):

- Google's Fake scenarios end to end through the binding (Shared ranges, NotShared, VerificationRequired, significant change, every error code).
- Error mapping with the Fake: each Google error code maps to its own category, and the Fake throws exactly `AgeSignalsException`.
- The Play manager is created lazily (Release build, real manager): with `Enabled = false` nothing bound to Play; the first bind came at the first `Evaluate` with `Enabled = true`, which returned `NotShared` with no error.
- Cancelling a call before it starts throws an `OperationCanceledException` subtype and writes no log entry.
- The real Play manager binds and returns a result on a sideloaded app (`NotShared`, no error).

iPhone (iOS 27.0.1, Apple sandbox account, Debug sample, gates 13/16/18; three runs on 2026-10-26 and 2026-10-27, UI test in tests/ios-device-uitests):

- In all three runs, for each of the six age scenarios the library returned `Shared` and reported the entry as logged (no error in runs 11 and 14; run 12's report did not record the error field): bucket `18+` / `Allowed` for the three 18+ scenarios, `16-17` and `13-15` / `AllowedWithParentalConsent`, `0-12` / `Blocked`. Runs 12 and 14 compared every row with the expected values and all matched; in run 11 the comparison was broken by a parser bug, but the recorded values match. Apple's sandbox showed no consent sheet in any run. In runs 12 and 14 the starting scenario was read from Settings and every switch and the final restore were read back. One device, one OS version, three runs.

iOS Simulator (headless, iOS 26.5; these runs predate the sample's scene adoption and have not been re-run with it):

- Apps link and load the Swift wrapper (no app-side native reference), and an enabled service reaches Apple's `requestAgeRange` without crashing.

## Unverified

- Replacing the workload-generated dSYM with the shipped one has not been tried.
- Pushing the main package's `.snupkg` together with the package using the short-lived trusted-publishing key (`dotnet nuget push` of a `.nupkg` also pushes the `.snupkg` beside it; not run against nuget.org).
- The GitHub-hosted CI and `release.yml` (publishing to nuget.org with trusted publishing) have not run yet: they run only in the public `ageledger/ageledger` repository and had not run when this version was prepared. Not yet seen on a hosted runner: the `xcode-27` image label (a preview image), the MAUI workload install pinned to workload version 10.0.300.3, the Android platforms on the image, the package checks, the hash hand-over between the build and publish jobs, and the nuget.org lookups. Pushing a `.snupkg` on its own by hand, and whether a "Push only new package versions" key is accepted for it, has not been tried; nor has whether a re-run attempt can upload an artifact under the same name been checked. The release has no automatic recovery; finishing a half-finished release by hand (CONTRIBUTING) has not been tried. The "already exists" message of the push script keys on the `Response status code does not indicate success: 409 (...)` text, which comes from the NuGet.Client and .NET runtime sources and has not been observed from nuget.org.
- Whether any released .NET iOS workload copies these dSYMs into an app's archive.
- No crash inside the Swift wrapper has been symbolicated with the shipped dSYMs; only `atos` on the dSYM against a wrapper function was checked.
- iOS: `RequestTimeout` with the sandbox Off scenario has been observed once, on one iPhone running iOS 27.0.1 with the Apple sandbox account (State Error, Error Timeout at about 15.1 s, 2026-10-27). Cancellation with Off has not been run on a device: the Cancel button was off screen in the UI test, and the button's new position has not been run.
- On an iPhone only the sandbox scenarios listed above ran (one device, one OS version, gates 13/16/18). Not run: answering or declining a consent sheet (none appeared in the sandbox), `IsEligibleForAgeFeatures` and the 26.4 regulatory calls, cancellation with a real sheet, the sandbox "Off" scenario with no timeout (with Off and no timeout, Evaluate did not return within 60 seconds in all three runs; in run 12 a system sheet "Age-Appropriate Experiences in Apps" was up in Settings, in run 14 none was and the sample was in the foreground; the cause is not confirmed; with `RequestTimeout` 15 s it ended with a Timeout error once, see the I18 row in the device test results), and any real (non-sandbox) account. The earlier iOS 26.5 Simulator results predate the sample's scene adoption. On 2026-10-27 a headless iOS 27.0 Simulator run launched the app, passed the self-test 20/20 and got Unavailable / NotAvailable from the real Apple backend through the internal binding, with no crash; no real age range or sheet answer was observed there. The library logic is otherwise covered by the fakes. On Android the real Play manager has not returned a real age signal (a licence tester or supported state); only the sideloaded `NotShared` path was observed. The Simulator's flow dismissed itself about 90 ms after being presented; why is not confirmed.
- Whether a `net10.0-android` app really gets a usable `Activity` through `Platform.CurrentActivity` for `AgeSharingConsentWrapperActivity` in all lifecycle states.
- iOS: whether cancelling or timing out the token while Apple's sheet is up behaves sensibly (the wait stops; the sheet stays until the person dismisses it). A token already cancelled when the main-thread work starts skips the sheet; this is compiled, not run.
- Android: the bounds Play returns are not assumed to follow the log's bucket rules. An unexpected range (equal bounds, lower above upper, a bound of 100 or more) is logged as `withheld` (a range was received but cannot be written); the returned `Bucket` is unchanged.
- `AgeBucketLogging.Withheld` and the `withheld` value have not run on a device; the recorded L10/L11 runs used the earlier `Unknown` value.
- iOS: with several foreground-active scenes (iPad Split View or Stage Manager, `UIApplicationSupportsMultipleScenes` true) the default presenter lookup picks an arbitrary active scene; such hosts should pass their own `presenter` (a `Func<UIViewController?>`) to the public `AppleAgeRangeBackend` constructor. `ShowSignificantUpdateAcknowledgmentAsync` always uses the default scene lookup and ignores `presenter`, so multi-scene hosts cannot steer it yet. Not run.
- iOS: calls from a background thread (the presenter and scene are resolved on the main thread; not run).
- iOS: what happens when no presenter exists yet at launch (reported as `AgeSignalsError.NoPresenter`; the foreground-inactive fallback is not run, and a background scene is no longer used).
- Added after the hardening batch and compiled but not run on a device: the `RequestTimeout` / `Timeout` path with a real sheet or consent screen, the Android `NoActivity` (finishing or destroyed activity) result and the main-looper start of the access request, and the iOS no-background-scene rule (rows H1-H8 in the device test plan). Earlier device results predate these changes; the Android no-activity case, recorded as `Other`, now reports `NoPresenter`.
- Android: whether the binding's `AsAsync` surfaces Google's `AgeSignalsException` as the managed type for the real Play manager. If it does not, every Android error maps to `Other` (the Fake was checked; the real manager's error path was not).
- iOS: the xcframework has arm64 slices only (device and simulator); there is no x86_64 simulator slice, so Intel Macs cannot run the simulator build.
- How Apple reports bounds when the lower gate is not crossed (`lowerBound` nil): treated as 0 in the label.
- That the dynamic framework is embedded and signed correctly in an app bundle on a device. The sample app builds for the Simulator and `Frameworks/AgeledgerNative.framework` is in the bundle with no `NativeReference` in the app (also when the app consumes a locally packed `Ageledger` nupkg, which carries the binding assembly and its `.resources.zip`); signing and running it on a device has not been checked.
- Texas civil-penalty amounts, the Texas court status, California AB 1856 status, Utah/Alabama signing dates, and the Play testing artifact's Maven coordinates.
- Whether the default `NoSignal`/`VerificationRequired` decisions suit any particular law. They are placeholders.

## Licence

Licensed under the Apache License, Version 2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE). Copyright 2026 Priyanka Bukka and contributors.

Security issues: see [SECURITY.md](SECURITY.md). Contributions: see [CONTRIBUTING.md](CONTRIBUTING.md).
