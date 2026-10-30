# Device test plan

Plan for showing that Ageledger works on real Android and iOS devices, using a minimal sample app. Android rows were run on a phone and some iOS rows headless on the Simulator on 2026-10-25 (docs/device-test-results.md); some iPhone rows were run on 2026-10-26 and 2026-10-27 (sandbox account, iOS 27.0.1; see device-test-results.md); the rest are listed as not run in the README 'Unverified' list. Not legal advice; the plan tests
the library's behaviour, not any app's legal position, and a passing test says nothing about compliance with any law.

To run the sample on your own devices (own bundle ID, own Apple account), see [try-the-sample.md](try-the-sample.md).

Facts below are labelled **verified** (read in Apple's or Google's documentation on 2026-10-25, section named),
**secondary** (only seen in a search summary or a forum), or **unverified** (inference; to be settled by the first run).

## 1. Goal and scope

Show, on real hardware, that:

1. The Android wrapper maps Google's results and errors to the neutral model, with Google's `FakeAgeSignalsManager`
   (every scenario) and with the real manager (the error paths a sideloaded build can reach).
2. The iOS wrapper presents Apple's sheet, returns the range and maps declines and errors, on a Simulator (as far as
   Apple allows) and on an iPhone with Apple's sandbox scenarios.
3. The decision log is written, exported and cleared with exactly five fields, `Enabled = false` does nothing, and the
   `Logged` flag, `AgeBucketLogging` and gate validation behave as documented.
4. The behaviours fixed after code review (section 8) work on devices: iOS cancellation, main-thread UIKit access,
   the `NoPresenter` error and scene fallback, and the lazily created Play manager.

Order of execution: Android emulator (Fake) -> Android phone (Fake, then real-manager error paths) -> iOS Simulator ->
iPhone (sandbox account scenarios).

Out of scope: any claim about legal compliance; real child accounts; Play Console and a real Play signal (a real
Android app on Play is planned later, once the sample has proved the library, so success rows that need it are
deferred, section 7.2); publishing to a store.

## 2. The sample app (`samples/AgeledgerSample`)

One page, one project, no network access, no analytics, no third-party packages beyond the library (the library itself references the AndroidX
`Xamarin.AndroidX.Lifecycle.LiveData.Core` version the Play binding needs, so the sample has no pin of its own). The sample exists and exposes:

- Switch "Enabled" (default off), switch "AgeBucketLogging: Exact / Withheld" (default Exact), and a picker of **valid**
  gate presets (for example `[18]`, `[13,16]`, `[13,16,18]`, `[12,14,17]`, `[2,4,6]`, `[97,99]`). Rule id (default `TEST`);
  app version is the build version. Changing a setting rebuilds the `AgeSignalsService`, so the constructor validation
  (only when Enabled) is exercised. A debug-only "invalid gates" preset (`[13,13]`, `[1]`, `[13,100]`, four gates)
  shows the constructor's `ArgumentException` (type name only) when enabled and no error when disabled.
- Button "Evaluate": calls `EvaluateAsync()` and shows `Result.State`, `Result.Bucket.Label`, `Result.Error`,
  `SignificantChange`, `ParentalControls`, `Decision` and `Logged`. Nothing else from the platform is shown or stored.
- Button "Evaluate, cancel after N ms" (N entered in a field, default 0, which cancels immediately; enter 2000 for 8.1(a)): starts `EvaluateAsync` with a
  `CancellationTokenSource` that fires after N ms and shows either the result or `Outcome: Cancelled` with the exception type
  name (`OperationCanceledException` or a subclass), with the elapsed time.
- Picker "Request timeout (ms)" (None, 5000, 15000 or 60000; default None; kept in the preference `request-timeout-ms`, so a relaunch keeps it) passed to
  `AgeSignalsOptions.RequestTimeout` when the service is rebuilt, and a button "Cancel" (beside Evaluate, so it is on screen without scrolling on a small phone) that cancels the most recent Evaluate still running (any of the Evaluate
  buttons) and sets the status line to "Cancel requested" or "Cancel: nothing in flight". The result label always ends with the elapsed time; a cancelled call shows `Outcome: Cancelled`, the exception type name and the
  elapsed time, and a timeout shows State `Error` with Error `Timeout`.
- Buttons "Export log" (shows the JSON array in a scrollable label, with copy to the clipboard) and "Clear log".
- Android debug builds only: a picker of Fake scenarios (section 7.1) that injects the fake with no library change:
  `new GoogleAgeSignalsBackend(() => Platform.CurrentActivity, manager: fake)` under `#if DEBUG`, and a "Real manager"
  option. That `FakeAgeSignalsManager` implements `IAgeSignalsManager` is confirmed only by compiling. The library's
  `Ageledger.Testing.FakeAndroidAgeSignalsBackend` is not used on devices because it bypasses the binding.
  Release builds use the real manager only.
- iOS: a button "Evaluate on a background thread" (`Task.Run`) and a checkbox "Evaluate at launch" (section 8). Buttons
  for `IsEligibleForAgeFeaturesAsync`, `GetRequiredRegulatoryFeaturesAsync` and `ShowSignificantUpdateAcknowledgmentAsync`
  (show the value or "n/a").
- Debug-only diagnostic button (Android): calls the Fake `IAgeSignalsManager` directly and shows `ex.GetType().Name`
  and the `InnerException` type names, never messages. The backend catches everything and returns only an error
  category, so without this button A10-A14 check only the category. Nothing from it is written to the log file.
- Other debug controls the matrix uses: point the log at an
  unwritable path (L7; use an existing file as a directory component); create leftover `*.tmp` files (L2b); a button
  seeding 1000 made-up valid entries and then appending one directly to `JsonFileDecisionLog` (L4); a backend built as
  `GoogleAgeSignalsBackend(() => null, manager: fake)` (A16); an Android "Evaluate at launch"; a `MinimumAge` field
  (A4c, I7b); a custom `SharedRangePolicy` toggle (A15).
- Log file: `FileSystem.AppDataDirectory/age-decisions.jsonl` via `JsonFileDecisionLog`. A debug-only button lists the
  file names in that directory (to check `ClearAsync` removes leftover `*.tmp` files; section 7.4).
- Bundle id and package name: the same string on both platforms (`com.ageledger.sample`), set in the tracked
  sample csproj; it is not secret. Only `CodesignKey` and `CodesignProvision` live in the untracked
  `AgeledgerSample.csproj.user`, so nothing account-specific is committed.
- Android: `minSdk` 23 (the library's minimum), `targetSdk` current. iOS: deployment target 15 (library), tested on
  26.x because the framework starts at 26.0.

- Debug builds also contain an on-device self-test that runs many of the rows below without touching the controls (sections 7.5 and 7.6).

Test data is made up. The sample never asks for or stores a birthdate, account or device id.

## 3. Devices and ground rules

Target every device by explicit serial/UDID, never "booted" or "default". Serials and UDIDs are not written in this
file: use `<ANDROID_PHONE_SERIAL>`, `<EMULATOR_SERIAL>`, `<IPHONE_UDID>`, `<SIM_UDID>` kept outside the repository.

| Device | Use | Notes |
|---|---|---|
| Android emulator (new AVD created for this project) | Fake scenarios, error mapping, `Enabled=false` check | The `android-36` images used are `google_apis` (no Play Store), so the real manager can only reach its error paths here (section 5.2) |
| Android phone (a test phone) | Fake scenarios, then the real manager sideloaded (expect `AppNotOwned`); the license-tester case is deferred | With two Android devices attached pass the explicit target to `dotnet build -t:Install` (`-p:AdbTarget="-s <serial>"`) |
| iOS Simulator (one created for this project) | Does the sheet appear, what does the API return (section 6.1) | iOS 26.5 and 27.0 Simulator runtimes were used |
| iPhone (the test phone) | Real sheet, Apple sandbox scenarios, launch/thread/cancel checks | Needs iOS 26.2 or later for sandbox testing; 26.4 or later for the regulatory calls |

Use only the devices listed here; never any other attached device or simulator. Use only devices and simulators
created for this test. Restore any device setting changed (sandbox account sign-in, Developer Mode, scenario, Wi-Fi,
locale). Never stop processes by name.

## 4. Build and install steps

Android (workload `maui` 10.0.300.3 (checked 2026-10-27); Android SDK platforms 35/36 and build-tools):

1. Create an AVD from the existing `android-36` `google_apis` image (see Prerequisites) and start it by explicit
   name; read its serial with `adb devices` and use `adb -s <serial>` for every command afterwards.
2. `dotnet build samples/AgeledgerSample -f net10.0-android -c Debug -t:Install -p:AdbTarget="-s <serial>"`.
3. Read the log in debug builds with `adb -s <serial> shell run-as <package> cat files/age-decisions.jsonl` (the `files/` path is
   confirmed: the self-test results file is read from it the same way), or use the in-app export.
4. Logs: `adb -s <serial> logcat -d`, filtered to the app's pid; keep only counts and error categories.

The sample needs one project setting of its own, already in the sample csproj (the Android LiveData version that MAUI 10.0.20 and the Play Age
Signals binding need comes from the library, so there is no pin in the sample):

- iOS: `ValidateXcodeVersion` is false because the .NET for iOS pack (26.5) expects Xcode 26.5 while the sample was built with
  Xcode 27.1 (checked 2026-10-25) (the library build is unaffected). Remove it once the pack and Xcode match.

iOS:

1. Simulator: `dotnet build -f net10.0-ios -r iossimulator-arm64` (on a Mac without a signing identity, add `-p:EnableCodeSigning=false`), then `xcrun simctl install/launch <SIM_UDID> ...`.
   The log file is under `xcrun simctl get_app_container <SIM_UDID> <bundle-id> data`.
2. iPhone: `dotnet build -f net10.0-ios -r ios-arm64` with manual signing (section 6.3), install with
   `xcrun devicectl device install app --device <IPHONE_UDID> ...`, copy the log out of the app container with
   `devicectl device copy from`, or use the in-app export.
3. The first iOS build runs `native/ios/build.sh` (needs Xcode, present); later builds skip it unless a Swift source or
   the script changed. The Simulator build already puts `Frameworks/AgeledgerNative.framework` in the app bundle
   without any `NativeReference` in the app; on the iPhone, check that it is copied and signed (README "Unverified" item).

## 5. Android

### 5.1 Fake manager (`Google.Android.Play.AgeSignals.Testing.FakeAgeSignalsManager`)

**Verified (Google, "Test the Play Age Signals API", 2026-10-25):** the fake replaces the production manager, with
`setNextAgeSignalsAccessResult` (`SHARED` / `NOT_SHARED`), `setNextAgeSignalsResult` (`setAgeLower`, tier, significant
change pending or approved) and `setNextAgeSignalsException`. The page's examples use SDK 0.0.4. The Microsoft
binding 0.0.4 (checked 2026-10-25) contains the `Google.Android.Play.AgeSignals.Testing` namespace and `FakeAgeSignalsManager`
(**verified**: type names found in the binding assembly), so no extra package is needed. **Verified (present in the
0.0.4 binding assembly):** `SetNextAgeSignalsAccessResult`, `SetNextAgeSignalsResult`, `SetNextAgeSignalsException`,
`SetNextRequestAgeSignalsAccessException` and the VerificationRequired constant. The access call fails through
`SetNextRequestAgeSignalsAccessException`; the check call fails through `SetNextAgeSignalsException`. Queued
values are **sticky** (verified on an Android phone: a later Evaluate without Apply returned the previous scenario
again); reset the fake before every scenario. The fake does not talk to
Play, so it also works on an emulator without Play Store.

Run each scenario in section 7.1 on the emulator and the phone. Keep the fake out of release builds (`#if DEBUG` in
the sample) so a shipped app cannot be pointed at it.

### 5.2 Real manager

**Verified (same Google page):** the live API returns `APP_NOT_OWNED` for apps not installed through Google Play.
License testers bypass that check if (a) the package name exactly matches an app configured in Play Console and
(b) the Google Account active on the device is a license tester. The page does not mention internal test tracks.

| Case | Needs | Expected |
|---|---|---|
| Sideloaded debug build, no Play Console app, phone | nothing extra | `Unavailable` / `AppNotOwned` (or `PlatformComponentMissing`) (expected before the run); observed on a phone: `NotShared`, no error |
| Same on the emulator image without Play Store | nothing extra | `Unavailable` / `PlatformComponentMissing` or `NotAvailable` |
| Sideload as license tester | Play Console app with the same package name, a tester account as license tester | `Shared`, `NotShared` or `VerificationRequired`, depending on the account; record |
| Installed from a Play test track | Play Console app, uploaded build | as above, without the tester bypass |

What the real manager returns for an adult account is **unverified** until run. Google states the API returns
signals for eligible users in Texas whose accounts were created after 2026-05-28 and for Brazil under its Digital ECA
rules; other US states will be announced (**verified**, same page). Outside those, expect `NotShared` or no range.

## 6. iOS

### 6.1 What Apple documents for the Simulator

- **Verified (Apple documentation, DeclaredAgeRange framework page, 2026-10-25):** the framework is listed as
  introduced in iOS 26.0, iPadOS 26.0, Mac Catalyst 26.0 and macOS 26.0. The framework, `AgeRangeService` and
  entitlement pages say nothing about the Simulator.
- **Verified (Apple, "Testing age assurance in sandbox", StoreKit documentation):** testing is done on a device:
  enable Developer Mode, Settings > Developer > Sandbox Apple Account, sign in, select the account, Manage, then
  "Age Assurance" or "Revoke App Consent". The page covers iOS and iPadOS 26.2 and later. It does not mention the
  Simulator.
- **Secondary (Apple Developer Forums, from search summaries; the forum pages themselves could not be opened):** in
  Oct 2025 a developer was told Age Range testing was not available in the Simulator; later posts say Simulator plus
  sandbox gave no real values, and some report `notAvailable` after the first prompt.
- **Conclusion:** Apple does not document Simulator support. Treat the Simulator as "may return `NotAvailable`". Run
  it once to record what happens (this also exercises the Swift wrapper, the binding and entitlement embedding) and
  rely on the iPhone for real values.

### 6.2 Entitlement and App ID

- **Verified (Apple documentation for `com.apple.developer.declared-age-range`):** the key is
  `com.apple.developer.declared-age-range`, "A Boolean value indicating whether your app may request a person's age
  range". To use it, "enable the Declared Age Range capability on your target in Xcode".
- **Settled (2026-10-26, from the installed profile):** for this project's own runs, the App ID `com.ageledger.sample` was
  created in its developer account with the Declared Age Range capability, and its development profile
  `AgeledgerSample Development` (iPhone included) grants `com.apple.developer.declared-age-range`. The old App ID and profiles were removed from the account
  the same day.
- **Settled (2026-10-25, from the installed profile, before the rename):** the entitlement was configured on the
  previous App ID and showed up in its development profile. So the capability is set in the developer account, not only
  in Xcode. Apple's public capability tables do not list it; the profile is the evidence.
- `Entitlements.plist` in the sample: `<key>com.apple.developer.declared-age-range</key><true/>` (as in the README).

### 6.3 Manual signing

Per the project rules: no automatic signing; a development profile for the sample, named `AgeledgerSample Development`,
created in the developer account (never committed). The one exception to the usual development-plus-App-Store pair: the
sample is never distributed, so no App Store profile is created. The Simulator needs no profile. The sample's App ID is
`com.ageledger.sample` (with the Declared Age Range capability).

History of this project's own setup (2026-10-26). Another developer needs their own bundle ID, App ID and profiles in
their own account, which [try-the-sample.md](try-the-sample.md) walks through:

- For this project's own runs, the App ID `com.ageledger.sample` and the profile `AgeledgerSample Development` were created
  and installed; the profile grants the declared-age-range entitlement and includes the test iPhone.
- A wildcard App ID `com.ageledger.sample.uitests.*` and the profile `AgeledgerSampleUITests Development` were created in
  the same account for the UI-test target.
- History: until 2026-10-26 the sample used another App ID and profile name from before the rename; those were replaced
  on 2026-10-26 (the iPhone was added to the old profile on 2026-10-25).
- Selecting a profile by name can pick an older profile with the same name, and the device install then fails. The build
  selects the profile by UUID (`-p:CodesignProvision=<UUID>`), set in
  `samples/AgeledgerSample/AgeledgerSample.csproj.user` (ignored by `*.user`, imported automatically by MSBuild).

Build with `-p:CodesignKey="<identity>" -p:CodesignProvision=<UUID from the local .csproj.user>`
(values kept in untracked files; the csproj applies `Entitlements.plist` to device builds) and check the signed app with
`codesign -d --entitlements :- <app>` that the key is present.

### 6.4 Apple sandbox scenarios (iPhone)

**Verified (Apple's sandbox page):** the sandbox returns these values; ranges are inclusive.

| Scenario | Lower | Upper | Declaration |
|---|---|---|---|
| Under 13, significant change approved | none | 12 | not stated |
| 13-15, significant change approved | 13 | 15 | guardianDeclared |
| 16-17, significant change declined | 16 | 17 | guardianDeclared |
| 18+, age not confirmed | 18 | none | not stated |
| 18+, age confirmed | 18 | none | not stated |
| 18+, age confirmed, significant change applicable | 18 | none | confirmed |

**Observed (iPhone on iOS 27.0.1, three runs (11, 12 and 14) on 2026-10-26 and 2026-10-27, results in `device-test-results.md`):** with gates 13/16/18 the ranges lined up as the section 7.3 table assumes (bucket `0-12` for "Under 13", `13-15`, `16-17`, `18+`), and no consent sheet appeared in the sandbox for any scenario in runs 11, 12 and 14. The Age Assurance page shows the chosen scenario only as a row value (`1` for the selected row, `0` for the others). Off is recognised by the Off row's value `1` (seen in run 14) or by all six long rows reading `0` (not seen on a device). Opening the sandbox account page in Settings asks for Touch ID, once per fresh Settings session. The sandbox reported a "significant change approval required" parental-control value for every scenario except the two 18+ ones that are not "applicable". With the sandbox scenario Off, Evaluate did not return within 60 seconds (in all three runs; in run 12 a system sheet titled "Age-Appropriate Experiences in Apps" was up in Settings during that time, in run 14 none was and the sample was in the foreground; the cause is not confirmed). Whether the library's timeout or cancellation covers this is not verified.

How these ranges line up with the app's own gates was **unverified** before that run; for other gates it still is. The expected labels in
section 7.3 assume gates 13/16/18, where Apple reports a lower bound of nil for the lowest range (the label then shows
0). The library drops the declaration by design, so the sample cannot show it.

## 7. Test matrix

Defaults unless stated: `Enabled=true`, `AgeGates=[13,16,18]`, `MinimumAge=13`, `AdultAge=18`,
`AgeBucketLogging=Exact`, `NoSignalDecision=NotApplicable`, `VerificationRequiredDecision=Blocked`.

Default policy: the **younger end** of the range decides. The lower bound (null counts as 0) below `MinimumAge` is
`Blocked`, below `AdultAge` is `AllowedWithParentalConsent`, otherwise `Allowed`; the upper bound is not used.

Every row with `Enabled=true` and a log appends one entry (errors too, with bucket `unknown`) and shows `Logged=true`, except cancelled calls (section 8.1 makes no entry) and L7.
With `AgeBucketLogging=Withheld` every entry's bucket is `withheld` when a range was received and `unknown` otherwise, whatever the display shows. Em = emulator, Ph =
Android phone, Sim = iOS Simulator, iPh = iPhone.

### 7.1 Android, Fake manager (Em first, then Ph)

| # | Fake setup | Expected state | Bucket | Decision |
|---|---|---|---|---|
| A1 | access SHARED, result 18 and older (adult tier) | Shared | `18+` | Allowed |
| A2 | access SHARED, 16-17 | Shared | `16-17` | AllowedWithParentalConsent |
| A3 | access SHARED, 13-15 | Shared | `13-15` | AllowedWithParentalConsent |
| A4 | access SHARED, 0-12 | Shared | `0-12` | Blocked |
| A4b | access SHARED, 0-17 (lower 0, upper 17) | Shared | `0-17` | Blocked (younger end) |
| A4c | A4b with `MinimumAge=18` | Shared | `0-17` | Blocked |
| A4d | access SHARED, 12-17 | Shared | `12-17` | Blocked (lower 12 is below 13) |
| A5 | access SHARED, no bounds | NotShared | `unknown` | NotApplicable |
| A6 | access NOT_SHARED | NotShared | `unknown` | NotApplicable |
| A7 | access VERIFICATION_REQUIRED (if the fake allows it) | VerificationRequired | `unknown` | Blocked |
| A8 | A3 plus significant change PENDING | Shared, SignificantChange=Pending | `13-15` | AllowedWithParentalConsent |
| A9 | A1 plus significant change APPROVED | Shared, SignificantChange=Approved | `18+` | Allowed |
| A10 | exception NETWORK_ERROR, set with `SetNextRequestAgeSignalsAccessException` (access call), then with `SetNextAgeSignalsException` (check call) | Error, Network | `unknown` | NotApplicable |
| A11 | exception API_NOT_AVAILABLE (both setters, as A10) | Unavailable, NotAvailable | `unknown` | NotApplicable |
| A12 | exception APP_NOT_OWNED | Unavailable, AppNotOwned | `unknown` | NotApplicable |
| A13 | exceptions PLAY_STORE_NOT_FOUND, PLAY_SERVICES_NOT_FOUND, CANNOT_BIND_TO_SERVICE, the version-outdated codes | Unavailable, PlatformComponentMissing | `unknown` | NotApplicable |
| A14 | exceptions CLIENT_TRANSIENT_ERROR, INTERNAL_ERROR | Error, Transient / Internal | `unknown` | NotApplicable |
| A15 | custom `SharedRangePolicy` returning Blocked, with A1 | Shared | `18+` | Blocked |
| A16 | debug option with `GoogleAgeSignalsBackend(() => null, manager: fake)` (no activity) | Error, NoPresenter (was Other before the NoActivity mapping; the earlier PASS predates the change, re-run) | `unknown` | NotApplicable |

A10 to A14 double as the error-mapping check in section 8 (managed type of the exception, only through the diagnostic button; otherwise the category only). Android does not use the
gate setting, so gate presets have no effect on these rows; an invalid preset still fails at construction when
enabled (L8).

### 7.2 Android, real manager (Ph and Em; error paths only)

Play Console and a real Play signal are out of scope for now: a real Android app on Play is planned after the sample
has proved the library. Success rows are therefore deferred.

| # | Device | Setup | Expected |
|---|---|---|---|
| A20 | Ph | sideload, real manager | Expected (before the run): Unavailable / AppNotOwned, decision NotApplicable, one log entry; observed: NotShared, no error (decision NotApplicable, one log entry) |
| A21 | Em (no Play Store) | sideload, real manager | Unavailable (PlatformComponentMissing or NotAvailable) |
| A22 | Ph | license tester, matching Play Console app | **Deferred** (needs Play Console); record state when done |
| A23 | Ph | Play Store disabled or signed out (restore afterwards) | Unavailable or Error; record category |

### 7.3 iOS

Gates 13/16/18 and the default policy unless stated.

| # | Device | Setup | Expected state | Bucket | Decision |
|---|---|---|---|---|---|
| I1 | Sim (iOS 26.5) | tap Evaluate | record: `Unavailable` / NotAvailable, InvalidAccount, Other, or a sheet; pass = no crash, a defined state, one log entry | `unknown` if error | NotApplicable if error |
| I2 | Sim (iOS 27.0) | same | same as I1 | | |
| I3 | iPh | sandbox "18+ age confirmed" | Shared | `18+` | Allowed (UI test, runs 11, 12 and 14: as expected) |
| I4 | iPh | sandbox "18+ age not confirmed" | Shared | `18+` | Allowed (UI test, runs 11, 12 and 14: as expected) |
| I5 | iPh | sandbox "16-17" | Shared | `16-17` | AllowedWithParentalConsent (lower 16 is at least 13, below 18) (UI test, runs 11, 12 and 14: as expected) |
| I6 | iPh | sandbox "13-15" | Shared | `13-15` | AllowedWithParentalConsent (UI test, runs 11, 12 and 14: as expected) |
| I7 | iPh | sandbox "Under 13" | Shared | `0-12` (lower nil shown as 0) | Blocked (UI test, runs 11, 12 and 14: as expected) |
| I7b | iPh | I6 with `MinimumAge=16` | Shared | `13-15` | Blocked |
| I7c | iPh | gates `[18]`, sandbox "13-15" or "16-17" (whichever Apple returns under that gate) | Shared | record, expected `0-17` | Blocked (younger end) |
| I8 | iPh | tap "Don't share" on the sheet | Declined | `unknown` | NotApplicable |
| I9 | iPh | sandbox account signed out | record (likely Unavailable) | `unknown` | NotApplicable |
| I10 | iPh | airplane mode | record (Network error, or a cached result) | | |
| I11 | iPh | valid gate presets from the picker (`[18]`, `[13,16]`, `[12,14,17]`, `[2,4,6]`, `[97,99]`) | sheet appears and the range label matches the gates; for `[2,...]` and `[97,99]`: a sheet, or Error / InvalidRequest; record | | |
| I11b | iPh | OBSOLETE: the service now copies its options when created, so a change to the caller's `AgeGates` afterwards has no effect and the sample's mutate control was removed. Covered by unit tests (`Step6HardeningTests`). | not run | | |
| I12 | iPh (26.2+) | `IsEligibleForAgeFeaturesAsync` | true, false or null; record per scenario | | |
| I13 | iPh (26.4+) | `GetRequiredRegulatoryFeaturesAsync` | bitmask or null; record per scenario | | |
| I14 | iPh (26.4+) | `ShowSignificantUpdateAcknowledgmentAsync` | Acknowledged, Unsupported or Failed; record | | |
| I15 | iPh | sandbox "Revoke App Consent" with the sample's bundle id | system shows "Notification Triggered"; nothing for the app to do (no server) | | |
| I16 | iPh | build without the entitlement | Error (InvalidRequest, Other or NotAvailable); record which | `unknown` | NotApplicable |
| I17 | iPh | iOS below 26.0 (only if such a device is available) | Unavailable / NotAvailable, no crash | `unknown` | NotApplicable |
| I18 | iPh | sandbox "Off", `RequestTimeout` set and, in a second pass, a cancellation token | record: does Evaluate return, time out or cancel instead of waiting indefinitely (open: with Off it did not return within 60 s in the UI-test runs). UI test, opt-in, section 7.7: pass 1 request timeout 15 s, waits up to 30 s; pass 2 no timeout, Cancel after 5 s. Observed 2026-10-27 (see device-test-results.md): in this one run, the timeout ended the wait at about 15.1 s (State Error, Error Timeout; one iPhone, iOS 27.0.1, sandbox). Pass 2 was not exercised: the Cancel button was off screen on the small display and the test never scrolls; the button has since been moved beside Evaluate, but that change has not been run | | |

### 7.4 Both platforms, log and flag

| # | Check | Expected |
|---|---|---|
| L1 | After A1-A16 (or I3-I8), Export | JSON array; every object has exactly `timestampUtc`, `ageBucket`, `decision`, `appVersion`, `ruleId`; the file is JSON Lines |
| L1b | Look at the exported timestamps and buckets | every `timestampUtc` ends in `Z`, with an optional fraction of up to 7 digits (writer format `yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'`, for example `2026-10-25T12:00:00Z`); a bucket such as `18+` appears with a literal `+`, not an escape such as `\u002B` |
| L2 | Clear, then Export | `[]`; file deleted |
| L2b | Before Clear, create leftover temp files in the app's data directory (name `age-decisions.jsonl.<hex>.tmp`, by a debug button), then Clear | the temp files are gone (checked with the debug file listing); unrelated files in the directory are untouched |
| L3 | Kill and relaunch the app, Export | entries persist |
| L4 | the debug button writing 1000 made-up valid lines in the writer's format to the log file and then appending one entry directly with `JsonFileDecisionLog` (works on both platforms; seeded so the row needs one flush to disk, not 1001) | 1000 entries kept, the oldest seeded one dropped, the newest is the appended one |
| L5 | `Enabled=false`, tap Evaluate | Disabled / NotApplicable; no sheet; no log entry; no platform call; `Logged=false` (section 8, item 5) |
| L6 | Two Evaluate taps in quick succession | no crash or corrupt line; on iOS see section 8 item 1(d) |
| L7 | `Logged` flag: with the log enabled, any Evaluate; then make the log unwritable (debug option pointing the log at a read-only path) | first `Logged=true` with one new entry; then result and decision still shown, `Logged=false`, no crash, no exception text on screen; restore afterwards |
| L8 | Validation only when Enabled: set an invalid preset (gates `[13,13]`, `[1]`, `[13,100]`, four gates, `[18,16]`; or an invalid rule id or version) | Enabled on: constructing the service throws `ArgumentException` (type name shown) and the previous service stays; Enabled off: no exception and Evaluate returns Disabled |
| L9 | Gate rules: valid `[2]`, `[2,4,6]`, `[97,99]`, `[18]`; invalid `[]`, `[1]`, `[100]`, `[13,14]` (less than 2 apart), `[13,16,18,21]` | valid sets construct; invalid ones are refused as in L8 |
| L10 | `AgeBucketLogging=Exact`, then `Withheld`, same scenario (A3 or I6) | the screen shows `13-15` both times; the exported entry shows `13-15` for Exact and `withheld` for Withheld; earlier rows are unchanged; the decision is the same |
| L11 | `AgeBucketLogging=Withheld` with a Blocked result (range received) | the entry has bucket `withheld` and decision `Blocked` (the decision can still imply an age group; record it as expected, not a defect) |
| L12 | Default policy, younger end: A4b (0-17) with `MinimumAge=18`, and I7c | `Blocked` |
| L13 | A run of the self-test (section 7.6), then look at the sample's saved settings and the manual log | unchanged: the self-test never writes the settings or the manual log |

### 7.5 Coverage by automation

Which rows the Debug sample's self-test runs by itself (section 7.6) and which still need a person or a tool that is not built yet. "Self-test" rows use fakes: on Android Google's `FakeAgeSignalsManager` behind the real `GoogleAgeSignalsBackend` (except the opt-in `real-play` suite, A20 and A21), on iOS the `Ageledger.Testing` package's `FakeAppleAgeRangeBackend`. A row that passes this way shows that the library's logic and, on Android, the binding code behave on the device; it says nothing about what Apple's sheet or Google's live service returns, so no item in the README "Unverified" list changes because of a self-test run. The expected values live in one table in the sample's source (`SelfTestRows.cs`), and a unit test checks that this table and the one below name the same rows.

| Row | Automated by |
|---|---|
| A1 | self-test (fake manager) |
| A2 | self-test (fake manager) |
| A3 | self-test (fake manager) |
| A4 | self-test (fake manager) |
| A4b | self-test (fake manager) |
| A4c | self-test (fake manager) |
| A4d | self-test (fake manager) |
| A5 | self-test (fake manager) |
| A6 | self-test (fake manager) |
| A7 | self-test (fake manager) |
| A8 | self-test (fake manager) |
| A9 | self-test (fake manager) |
| A10 | self-test (fake manager; the managed exception type names are recorded) |
| A11 | self-test (fake manager; type names recorded) |
| A12 | self-test (fake manager; type names recorded) |
| A13 | self-test (fake manager; type names recorded) |
| A14 | self-test (fake manager; type names recorded) |
| A15 | self-test (fake manager) |
| A16 | self-test (fake manager) |
| A20 | self-test, opt-in `real-play` suite (real manager; recorded, not asserted) |
| A21 | self-test, opt-in `real-play` suite (real manager on an emulator; recorded, not asserted) |
| A22, A23 | manual |
| I1, I2 | manual (Simulator) |
| I18 | UI test, opt-in (`scripts/ios-uitests.sh --row i18`, section 7.7); pass 1 run on a device 2026-10-27; pass 2 (Cancel) not exercised yet |
| I3, I4, I5, I6, I7 | UI test (`scripts/ios-uitests.sh`, section 7.7; runs 11, 12 and 14 on one iPhone, bucket and decision as expected) |
| I7b, I7c, I8 | UI test (planned). I8 needs a consent sheet, which did not appear in any sandbox run (11, 12, 14) |
| I9, I10, I16, I17 | manual |
| I11, I12, I13, I14, I15 | UI test (planned) |
| L1 | self-test (Android: fake manager; iOS: fake backend) |
| L1b | self-test (Android: fake manager; iOS: fake backend) |
| L2 | self-test (Android: fake manager; iOS: fake backend) |
| L2b | self-test (Android: fake manager; iOS: fake backend) |
| L3 | self-test (host-driven: the script ends the app between a write run and a read run) |
| L4 | self-test (Android: fake manager; iOS: fake backend) |
| L5 | self-test (Android: fake manager; iOS: fake backend; no platform call is counted) |
| L6 | self-test (Android: fake manager; iOS: fake backend; two services, one log) |
| L7 | self-test (Android: fake manager; iOS: fake backend) |
| L8 | self-test (Android: fake manager; iOS: fake backend) |
| L9 | self-test (Android: fake manager; iOS: fake backend) |
| L10 | self-test (Android: fake manager; iOS: fake backend) |
| L11 | self-test (Android: fake manager; iOS: fake backend) |
| L12 | self-test (Android: fake manager; iOS: fake backend) |
| L13 | self-test (settings and log snapshot) |
| 8.1b | self-test (plan item 8.1(b), pre-cancelled token: only OperationCanceledException, no platform call, no log entry, short time; "no sheet shown" needs the UI test) |
| 8.1 (a), (c), (d), (e) | UI test (planned) |
| 8.2 | manual (Main Thread Checker run); the Android worker-thread part is H2 |
| 8.3 | UI test (planned) for iOS; the Android part is A16 |
| 8.4 | see A10 to A14 |
| 8.5 | host-assisted (logcat and dumpsys counts compared by hand) |
| H1, H3, H4, H5, H7, H8 | manual |
| H2 | self-test (fake manager) |
| H6 | see 8.1b, that is item 8.1(b) (the sheet part needs the UI test) |

### 7.6 Running the self-test

The self-test is compiled into Debug builds of the sample only (`#if DEBUG`, in its own files; nothing in the library). It runs when the app is launched with a suite name and a random nonce, and ignores any other launch. It builds its own `AgeSignalsService` instances in code, so the page's controls and saved settings are not read or changed (row L13 checks that), and evaluate-at-launch is skipped while it runs. It logs into a `selftest/` folder of the app data directory and deletes the folder afterwards. Every service has a `RequestTimeout`, and the whole run has a 300 second limit. It uses fakes, except the opt-in `real-play` suite on Android (below); no row calls Apple's real backend, so no Apple sheet can appear. A run starts at most once per process, and on Android the launch extras are ignored when the activity is recreated or reopened from the recents list, and are removed once read.

In a Debug build any app on the same device could start the self-test through the exported launcher activity. It only runs the checks above and writes the results file inside the sample's private storage, but do not leave a Debug build installed on a device you do not control.

The result is `selftest-results.json` in the app data directory, written to a temporary name and renamed, even when the run fails part-way (a failed `run` case with the exception type name). The top-level fields are `formatVersion`, `nonce`, `suite`, `platform`, `osVersion`, `complete`, `deadlineHit`, `elapsedMs`, `expectedCases`, `passed`, `failed` and `cases`. Each case has `row`, `case`, `source`, `asserted`, `expectedState` and `state`, `expectedBucket` and `bucket`, `expectedDecision` and `decision`, `expectedLogged` and `logged`, `expectedError` and `error`, `expectedSignificantChange` and `significantChange`, `exceptionType`, `detail` (only `name=integer` pairs, enforced in code), `elapsedMs` and `pass`. It never holds a device name, serial number, account, Android ID or exception message. The host script fails when the number of cases differs from `expectedCases`. The screen shows `SELFTEST DONE <nonce>` at the end. A case with `asserted` false is recorded, not asserted: the table prints RECORD instead of PASS.

Host scripts drive a run and print a table of row, state, bucket, decision, logged and pass; they exit non-zero if any row fails, the run is incomplete, the nonce does not match or the deadline passes:

- `scripts/selftest-android.sh <serial>` and `scripts/selftest-ios.sh <udid>`. Install a Debug build of the sample first (section 4); the Android script stops at once if `run-as` is refused (a Release build). Each script refuses a device that is not listed in `scripts/devices.local` (untracked; copy `scripts/devices.local.example` and put your own serials and UDIDs in it), takes a lock for the device (directory `$AGELEDGER_LOCK_DIR`, default `/tmp/device-locks`, so other users of the device can see it is in use), and on iOS refuses to start while an `xcodebuild` test for that UDID is running. It ends only the sample's own process, launches it with a fresh nonce, waits, copies the results out (`run-as` on Android, `devicectl device copy from` on iOS), checks the nonce and prints the table. If the main suite cannot be launched or times out, the restart check is skipped.
- L3 (entries survive a restart) is done by the script: one run writes entries, the script ends the app, and a second run in a new process reads them back.
- `scripts/selftest-android.sh <serial> --real-play` also runs the opt-in `real-play` suite (A20 on a device, A21 on an emulator). That calls the real Play service, may show Play's own consent screen, and its answer can describe a real account. Only the state, error category and `Logged` are recorded; the bucket, decision and significant change are dropped before the result is written. Record only the state and error category from it in `docs/device-test-results.md`.
- The phone must be unlocked and awake for the launch. When a launch fails the scripts print only the problem lines of the launch output, with the device id and quoted strings redacted; do not paste launch output into the docs. The scripts do not change any device setting.

Run on 2026-10-25 with this version of the self-test (including the per-case count check), default suites without `--real-play`: on an Android phone running Android 13, the main suite passed 57 of 57 cases (complete, no case-count mismatch), the write step of the restart check 1 of 1 and the read step 1 of 1, the script exited 0, about 93 seconds in all; on an iPhone running iOS 27.0.1 the main suite passed 20 of 20 (including L13, complete, no mismatch), the restart check 1 of 1 and 1 of 1, exit 0, about 73 seconds. Passing the launch arguments to `devicectl`, the iOS results path (`Library/selftest-results.json`) and the Android `am start` component worked as written. These runs used the fake manager and fake backend only; the `real-play` suite was not run and no Apple sheet can appear in them. They say nothing about Apple's sheet or Google's live service.

### 7.7 Running the iPhone UI test

`tests/ios-device-uitests` holds a small UI-test project (XcodeGen `project.yml`; the Xcode project is generated and not tracked). `scripts/ios-uitests.sh --udid <udid> --test ExplorationTests/testExploration --app <path to the Debug sample .app>` runs it on a physical iPhone. Before the first run:

- Copy `tests/ios-device-uitests/signing.local.example` to `signing.local` (untracked) and fill in the team and the provisioning profile. One wildcard App ID, `com.ageledger.sample.uitests.*`, with a development profile ("AgeledgerSampleUITests Development"), has to cover every UI-test bundle id: the host app (`com.ageledger.sample.uitests.host`) and the runner Xcode derives from the test bundle (`com.ageledger.sample.uitests.xctrunner`). List the device in `scripts/devices.local` (untracked). The script refuses a device that is not listed, takes the shared per-device lock (directory `AGELEDGER_LOCK_DIR`, as in the self-test scripts), and refuses to start while another `xcodebuild` test for that device is running.
- A Sandbox Apple Account must be signed in on the phone (Settings > Developer), and the phone unlocked and plugged in. Turn on Do Not Disturb for the run: a notification banner over the sample or Settings can cover a control or end up in a screenshot of the sample.

Opt-in row I18 (section 7.3): `scripts/ios-uitests.sh --udid <udid> --row i18 --app <path to the Debug sample .app>` runs `I18Tests/testI18` instead of the default test (`--row` and `--test` exclude each other). It uses the same restore marker, lock, Touch ID wait and attachment allowlist as the default run. Before it touches Settings it relaunches the sample and checks that the timeout picker and the Cancel button exist (an older install stops the run with `NOTHING-CHANGED`), and it reads the sample's "Evaluate at launch" switch (read only: the test never taps it or scrolls to it, because a swipe could land on Apple's age sheet, which an evaluating relaunch can raise). If the switch is on or cannot be read, the sample is stopped and the run ends with `NOTHING-CHANGED` and the reason "Evaluate at launch is on; turn it off in the sample and re-run" (shown as `stopped by` in the summary): turn it off by hand in the sample first. It then selects Off (reading it back), relaunches the sample, turns Enabled on, picks gates 13/16/18 and sets the sample's request timeout to 15000 ms. It taps Evaluate, waits up to 30 s and records the state, error and elapsed time. In a second pass it sets the timeout to None, taps Evaluate and, after about 5 s, taps the sample's Cancel button, which cancels the most recent Evaluate still running; the sample's status line then reads "Cancel requested" or "Cancel: nothing in flight", and the test records which one appeared. Cancel is not tapped, and nothing is scrolled for it, when a result is already shown, a system sheet was seen, the button is not hittable where it is, or the sample is not in the foreground; the pass is then "unknown" with the reason in its note. Each pass is recorded as `returned`, `timed-out`, `cancelled`, `not-evaluated`, `exception` or `no-result` (nothing shown after the wait: it did not return), together with the elapsed time the sample reported and the time the test waited. The sheet probe covers the first 3 s after the tap and one second just before Cancel; a sheet that appears later is only noted in the Settings trail (labels from the fixed list), and a sheet seen at any point is left unanswered and makes the pass "unknown". The row is a recording, not a pass or fail. At the end, in a separate teardown step that runs after the scenario restore (so a problem there cannot skip the restore), it puts the sample's timeout back to None with read-back, reported as `sample timeout reset` in the summary (`read-back`, `failed` when it could not be set or the picker was not hittable without scrolling, or `not applicable` when the sample has no picker),. The scenario is restored first, reading the selection back. The age-scenario rows of the default test also set the sample's timeout to None after each relaunch when the sample has the picker, so a leftover timeout cannot affect them. The summary line per pass starts with `i18-timeout` or `i18-cancel`. The timeout picker is set through the same wheel handling as the gates; that it reads back correctly on a device is not verified.

What a run does: it reads the current Age Assurance scenario, switches through the scenarios, relaunches the sample, taps Evaluate for each, looks for Apple's consent sheet, and compares the sample's bucket and decision with the expected values for the scenario. It then sets the scenario back to the one it started from. On iOS 27.0.1 the Age Assurance page shows the selected scenario as a row value (`1`, the others `0`); Off is recognised by the Off row's value `1` (seen in run 14) or by all six long rows being present and reading `0` (not seen on a device), otherwise the scenario is unreadable. The starting scenario is read from the page and a restore counts only when the selection reads back. For an OS version without that signal, `--assume-original-off` takes an unreadable starting scenario to be Off; without the flag the run stops before changing anything.

Restore safety: just before the test starts, the script writes "unknown" into a small file, `pending-restore-<id>`, in `~/Library/Application Support/ageledger-uitests/` (outside the repository and outside `/tmp`, so neither a clean checkout nor a reboot deletes it; the per-device locks stay in `AGELEDGER_LOCK_DIR`, then `DEVICE_LOCK_DIR`, then `/tmp/device-locks`). The test prints `ORIGINAL-SCENARIO: <label>` as soon as it has read the scenario, and the script replaces "unknown" with that label at once (the line was seen live in the log in the device runs so far; "unknown" covers a run where it does not arrive). The file is deleted only after a restore read back, or when the test says `NOTHING-CHANGED` (it threw before it touched the scenario). A failed or interrupted run is followed by a separate restore run. The next run restores a label first; if the file still says "unknown" it refuses to start. To clear a stuck file by hand, check the scenario on the phone (Settings > Developer > Sandbox Apple Account > Age Assurance), set it right if needed, then delete the file. A row whose scenario switch could not be read back is recorded as "unknown", never as a pass. If neither the first nor the second Evaluate showed a consent sheet there was no answer to remember, so later rows whose switch read back are "recorded"; if a sheet showed once and then not again (a remembered answer), later rows are "unknown".

The phone asks for Touch ID when the sandbox account page opens in a fresh Settings session, so the person holding the phone is needed for at least one touch per run (more if iOS locks the page again while Settings is in the background). SpringBoard can report a Touch ID prompt element while nothing is on screen (seen in two runs, where the person holding the phone saw no prompt), so the prompt counts as up only when that element exists, is hittable and has a non-empty frame inside the screen. If it exists but is not on screen, the test logs "authentication alert element present but not on screen; continuing to wait for the Manage screen" once and keeps waiting. The wait ends with `manage-screen` (recorded in the trail) only after one of these markers has been present with no prompt on screen for 2.5 s of polls in a row (five polls 0.5 s apart; any poll with the prompt on screen starts the count again): a row labelled "Age Assurance" or "Revoke App Consent", a navigation bar titled "Manage" or "Account Settings", or the Age Assurance page itself. Manage has been seen to open the "Account Settings" page with no prompt; its title as a plain text counts for the navigation check that follows, but not for ending this wait. The wait also ends with `alert-dismissed` when the element goes away. At the 120 s limit it stops with an error only if a prompt is really on screen; if only an element that is not on screen is left, the trail records `limit-without-visible-alert` and the next screen check decides. No tap in Settings is made while the prompt is on screen, and an element with the prompt's identifier that is not on screen is not treated as an unexpected alert (every other alert still stops the run). The script prints "ACTION NEEDED: touch the Touch ID sensor on the iPhone now (120 s)" only when the prompt is really up; the test waits up to 120 seconds, never taps Cancel, and if the prompt is still up afterwards stops without tapping anything. The line streamed into the log at the time in the device runs so far, but watch the phone as well. Long runs should be started in the background.

Apple's own sheet "Age-Appropriate Experiences in Apps" (buttons such as Continue and Update Birthday) has been seen in Settings during the Off scenario. The test treats it as a known system sheet: it records its known labels, taps nothing, closes Settings so the sheet goes away, stops the remaining rows, restores the scenario (reading the selection back), and fails the run so the stop is visible. The script prints "A system age sheet appeared on the phone; leave it unanswered".

A consent-sheet button is tapped only inside a container that, when looked at again just before the tap, has a text or button label containing the phrase "age range"; that check has not been seen against a real sheet yet (none appeared in the sandbox runs). "Share" counts as a share button only as exactly "Share", "Don't Share" or "Share Age Range".

What is recorded: the test takes a screenshot of the sample only when the sample is in the foreground, Settings is not, and neither Settings nor SpringBoard shows an alert or sheet, checked before and after the capture (run 12 kept one screenshot that showed a system sheet in Settings because an earlier check was weaker; the picture was deleted and the check has been tightened since, but that change has not been run on a device). Kept screenshots are not text-scanned by the script. Of Settings and of any system alert the test records labels from a fixed list of known Apple labels and counts of everything else; of a candidate consent sheet it records button labels and a count of the texts. It reads no text-field or secure-field values. The script deletes every attachment that is not on its allowlist and checks the rest for "@" and device identifiers before printing a summary; it also removes the team id and the signing-identity and profile lines from its output. The name given in `scripts/devices.local` is only used to scrub output, so it has to match what the device reports about itself. Results land under `bin/ios-uitests/`. The restore marker is per macOS user (it lives under that user's `~/Library/Application Support`), so run the UI test from one account.

The label parser can be checked without a device: `tests/ios-device-uitests/check-result-parser.sh` compiles `ResultParser.swift` with a small check and runs it (it is not part of CI).

## 8. Device checks for fixed behaviour

Each was a defect or risk found in code review and has since been fixed in the library. Unit tests with fakes cannot
show it on a device, so each is now a check that the fixed behaviour holds. All are **unverified until run**.

1. **iOS cancellation while the sheet is up.** The backend returns `tcs.Task.WaitAsync(cancellationToken)`, so
   cancelling stops the wait; the sheet itself cannot be dismissed from code and stays until the person acts. If the
   token is already cancelled when the main-thread work starts, the sheet is skipped. Test on the iPhone (and the
   Simulator if a sheet appears) with "Evaluate, cancel after N ms":
   (a) N = 2000 with the sheet up: expect `OperationCanceledException` after about 2 s, the sheet still on screen, and
   no log entry for that call; answering the sheet afterwards does nothing and does not crash;
   (b) N = 0 (token already cancelled; 1 ms races): expect cancellation and **no sheet shown**;
   Android phone (Fake), N = 0: threw `TaskCanceledException` (an `OperationCanceledException`), no log entry;
   (c) the app sent to the background and back with the sheet up;
   (d) a second Evaluate while the sheet is up: record what Apple does (second sheet, queued, or error); no crash;
   (e) a normal Evaluate after a cancelled one works.
   On Android with N = 0: expect `OperationCanceledException`, or record what happens.
2. **UIKit access on the main thread (iOS).** Scenes, windows and view controllers are read inside the main-thread
   block. Test: the sample button running `Task.Run(() => service.EvaluateAsync())`, with the Main Thread Checker
   enabled in a debug run on the iPhone and the Simulator. Expect a normal result or sheet and no Main Thread Checker
   report. Repeat 20 times. Do the same on Android (`Platform.CurrentActivity` from a worker thread); record any
   problem.
3. **No presenter at launch: `NoPresenter` and scene fallback.** The backend returns `NoPresenter` (not
   `InvalidRequest`) when no view controller can be found, after trying a foreground-active scene, then an inactive
   foreground scene (a background scene is never used). Test with "Evaluate at launch" (call from `FinishedLaunching` or the page
   constructor, and again after a short delay). Expected results:
   - if no scene or window exists yet: Error / `NoPresenter`, bucket `unknown`, one log entry;
   - if the scene is `ForegroundInactive` (launch, app switcher, system banner showing): the fallback finds it and the
     sheet appears, or Error / `NoPresenter` if even that fails; record which (a scene in the background gives `NoPresenter`);
   - once active: a normal sheet.
   Never `InvalidRequest` for these cases. The sample now uses the scene lifecycle: a call made from `FinishedLaunching`
   always gives `NoPresenter` (no scene is connected yet); the page constructor runs during
   `scene:willConnectToSession:`, and the posted (`BeginInvokeOnMainThread`) lookup runs after that returns, when the
   scene is usually `ForegroundInactive`. A sheet or `NoPresenter` are both acceptable there; record which.
   Android equivalent: the null-activity backend (A16) gives Error / `NoPresenter` every time (also for a finishing or destroyed activity); also try an Android "Evaluate at
   launch" and a paused activity and record (MAUI keeps `CurrentActivity` set while paused, and null from `OnCreate` is
   unreliable).
4. **Android error mapping.** `GoogleAgeSignalsBackend.MapError` (which maps the code through `AndroidCodeMap`) walks the exception chain looking for the bound
   `AgeSignalsException`. Whether a failed Google task arrives as that type, a generic `Java.Lang.Exception`, or a
   wrapper with an inner exception is unverified. Test with the fake throwing each `AgeSignalsErrorCode` (A10-A14) and
   the diagnostic button showing the managed type names (without it only the category is checked); every row must map to its own category, not `Other`. Also check that
   `ErrorCode` constants compare as the right numeric type, that cancellation still yields
   `OperationCanceledException`, and that both a failed task and a synchronous throw map.
5. **Lazy Play manager (`Enabled=false` creates no manager).** The backend creates the Play manager on the first
   request (a `Lazy`), so building it at startup, as the README snippet does, no longer touches Play. Test on the
   emulator and phone with the flag off in two modes: (a) the backend is constructed at startup, (b) it is never
   constructed. The sample needs no extra toggle: a Debug build with the default Fake covers (b) (the backend never
   constructs the Play manager), and a Release build, which uses the real manager, covers (a). Compare `adb -s <serial> logcat` (Play services and Play Store tags, bind attempts) and
   `adb -s <serial> shell dumpsys activity services <package>`. Expect (a) and (b) to look the same: no bind, no Play
   traffic, no result, no log entry. Then turn Enabled on and Evaluate: the manager is created and the first bind
   appears. Same check on iOS: no `AgeRangeService` call and no sheet when off.

## 9. What only real accounts in Texas or other US states can show

The sandbox and the fake cover the API shapes. These need real, region-bound accounts and cannot be proved with test
accounts or by changing a device's region:

- Real Play Age Signals for a Texas user: Google says signals are returned for eligible users whose accounts were
  created after 2026-05-28 (verified, Google page). The real `Shared` ranges and tiers, `VerificationRequired` for a
  real supervised or unverified account, and what a `NotShared` user in Texas looks like.
- Real Family Sharing child accounts on Apple: the parent's "always share / ask / never share" choices, the real
  consent sheet for a child, and Apple's override of the app's gates by local regulation (Apple's `AgeRangeService`
  page says the system may override age gates based on the person's location; verified).
- `requiredRegulatoryFeatures` and `isEligibleForAgeFeatures` values for a real account in a regulated state (the
  sandbox scenarios return age ranges; whether they also exercise region-specific features is unverified).
- Real significant-change approval and revocation flows through a parent's device, and the PermissionKit side.
- Behaviour after a state's operative date (see the README's background table), and which accounts a store treats as
  in scope (new accounts only, or existing ones).
- Real Play distribution: signals for an app installed from Play, with the Play Console age-band configuration.

Not to be done: creating accounts for minors, using a child's account or devices, spoofing location, or using another
person's account. If real-account evidence is wanted, it must come from adults' own accounts with their explicit
consent, and only the five logged fields would be recorded.

## 10. Recording results

For each row record: date, platform and OS version (no device name, serial or account), build identifier, state,
bucket, error category, decision, whether a log entry was written, pass or fail against the expectation, and notes.
Store results under `docs/` without identifiers. After the runs, update the README "Unverified" list: move proven
items out, keep unproven ones, and add any new finding from section 8. Raw logcat and console output stays untracked
and is scrubbed before anything is quoted.

## 11. Added after the hardening batch (not run on a device)

One system UI at a time per service: after a timeout or cancel the UI may still be showing and the next call of either kind waits for it; a repeated Evaluate gets the answer only if it has joined that request when the person answers (a call queued behind another call or a significant-update call does not join); an answer with no call joined is discarded and the next call asks again.

| # | Platform | Step | Expected |
|---|---|---|---|
| H1 | Android, iPh | `RequestTimeout` of a few seconds, leave the consent screen or sheet unanswered | Error / `Timeout` after the limit, one log entry, decision per `NoSignalDecision`; the system UI stays until dismissed |
| H2 | Android | Evaluate from a worker thread (`Task.Run`) | access request starts on the main looper; a normal result; no crash |
| H3 | Android | Evaluate while the activity is finishing (finish, then call) | Error / `NoPresenter` |
| H4 | Android, iPh | two Evaluate taps in quick succession | a second sheet or screen only after the first is answered; the second call waits (and ends in `Timeout` if a timeout is set and the first is still unanswered) |
| H5 | iPh | Evaluate while the only scene is in the background | Error / `NoPresenter`, no sheet |
| H6 | iPh | pre-cancelled token | OperationCanceledException, no sheet, no log entry |
| H7 | Android, iPh | `RequestTimeout` of a few seconds; after the Timeout result, tap Evaluate again at once while the sheet or consent screen is still up | no second sheet or screen; answer within the second call's timeout: it completes the second call with that answer (two rows: `Timeout`, then the answer); otherwise two `Timeout` rows |
| H8 | Android, iPh | `RequestTimeout` of a few seconds; after the Timeout result answer the sheet or consent screen, wait, then tap Evaluate | the answer is discarded (no row); a new request starts; record whether a second sheet or screen appears |

## Prerequisites

- Android: an AVD created from an `android-36` `google_apis` image (there is no Play Store image, so the real manager only reaches its error paths on the emulator), or a phone.
- iOS Simulator: a Simulator device (iOS 26.5 or 27.0 runtime) created for the test.
- iPhone: the development profile `AgeledgerSample Development` (section 6.3), Developer Mode on, and a Sandbox Apple Account signed in (Settings > Developer > Sandbox Apple Account; the account is created in App Store Connect > Users and Access > Sandbox). Restore the previous setting afterwards.
- The `maui` workload covers both platforms; no other installs are needed.

Not needed: a Play Store system image, Play Console, an App Store profile.
