# Try the sample app on your own devices

This guide is for a developer outside this project who wants to run the sample app (`samples/AgeledgerSample`) on their own iPhone or Android device, and optionally the iPhone UI test (`tests/ios-device-uitests`). It is the setup walk-through only; what the rows of the device test mean and what was observed is in [device-test-plan.md](device-test-plan.md) and [device-test-results.md](device-test-results.md).

Nothing here is legal advice, and running the sample does not make any app compliant with any law. The sample uses test data only and sends nothing anywhere itself.

Links to Apple and Google pages were checked on 2026-10-27 (see the list at the end). Where a page's body could not be read by the tool used for the check, this guide says so and the steps come from this repository's own notes instead.

## 0. Before you start

- You will build the sample from a clone of this repository, with your own bundle ID (section 2) and your own Apple signing (section 3). The identifiers this project uses are registered to its own Apple developer team, and Apple does not let another team register the same ones.
- Placeholders used below: `com.example.agesample` (your bundle ID), `YOUR_TEAM_ID`, `<IPHONE_UDID>`, `<PROFILE_UUID>`. Keep real values out of anything you commit.
- The sample is a Debug app for testing. A Debug build can be told to run its self-test by any app on the same device, so do not leave it installed on a device you do not control.

## 1. iOS prerequisites

- An iPhone on iOS 26.2 or later for sandbox testing; 26.4 or later for the regulatory calls (`GetRequiredRegulatoryFeaturesAsync`, `ShowSignificantUpdateAcknowledgmentAsync`). Below iOS 26 the library reports Unavailable (README, "Requirements for an app"; device-test-plan.md section 3).
- An Apple Developer Program team you can sign with. This project's own runs used a paid membership. Whether a free account can enable the Declared Age Range capability for a device build was **not checked** so plan on a paid membership. The steps in section 3 need the Account Holder or Admin role (Apple's pages for registering App IDs, devices and profiles say so).
- A Mac with the tools the README and `global.json` describe: .NET SDK 10.0.300 (`global.json`, roll forward to the latest patch), the `maui` workload (this repository's runs used workload version 10.0.300.3; the 26.5 .NET iOS pack), and Xcode with the iOS 26.4 SDK or newer (the Swift wrapper will not build with an older SDK; this project's runs used Xcode 27.1). The sample turns `ValidateXcodeVersion` off because the pack and Xcode versions differed in our setup.
- XcodeGen, only for the UI test in section 6 (`scripts/ios-uitests.sh` runs `xcodegen generate`).
- A USB cable. Trust the Mac on the iPhone when asked.

## 2. Replace the bundle ID

Our bundle ID, `com.ageledger.sample`, and the UI-test IDs `com.ageledger.sample.uitests.*` are registered to our team. Choose your own, for example `com.example.agesample`, and set it in **one** place: create the untracked file `scripts/sample-id.local` (it is in `.gitignore`) containing only the id on one line:

```
echo com.example.agesample > scripts/sample-id.local
```

The id uses letters and digits in dot-separated parts, each starting with a letter (no hyphens, which Android packages do not allow); only the first line of the file is read. A wrong id stops the sample build and the scripts with an error.

That one value is used by:

- the sample's `ApplicationId` (`samples/AgeledgerSample/AgeledgerSample.csproj`): the iOS bundle ID and the Android package name. The Android activity has no fixed name that includes the package, so nothing else in the app needs editing;
- `scripts/selftest-android.sh` and `scripts/selftest-ios.sh` (the package they start and read from) and `scripts/ios-uitests.sh` (the app it installs, and the only app `--reinstall-probe` uninstalls);
- the UI-test project: the XcodeGen project (`tests/ios-device-uitests/project.yml`) uses it as the prefix, `<your id>.uitests.host` for the host app and `<your id>.uitests` for the test bundle, and `SampleApp.swift` attaches to the app with it.

Instead of the file you can set the environment variable `AGELEDGER_SAMPLE_ID` (it wins over the file), or pass `-p:AgeledgerSampleId=com.example.agesample` to a single `dotnet build`. That `-p:` value affects only that build; the scripts read just the environment variable or the file, so the file is the one to use. The UI-test project defaults to `com.ageledger.sample` as the build setting `AGELEDGER_SAMPLE_ID`; `scripts/ios-uitests.sh` passes yours, but if you generate and run that project by hand (`xcodegen generate`, Xcode) set the build setting, and in Xcode also add `AGELEDGER_SAMPLE_ID` to the scheme's Test environment variables (`SampleApp.swift` reads the runner environment, not the build setting), or the default is used. If nothing is set, the default `com.ageledger.sample` is used. After changing the id, build again (and regenerate the UI-test project) before installing.

Other mentions of our IDs are in the documentation (`docs/device-test-plan.md` and this guide describe this project's own IDs); nothing needs editing there. `com.ageledger.native` (the framework's own identifier inside the package, set in `native/ios/build.sh`) does **not** need changing.

## 3. Apple developer account steps (iPhone)

Do these in your own account, in [Certificates, Identifiers & Profiles](https://developer.apple.com/account/resources). Keep the profile names in the style `<App> Development`; the project rule here is manual signing with profiles you can see in the account, not Xcode-managed automatic signing.

0. **Have an Apple Development certificate** (skip if you already have one in your keychain). In Certificates, Identifiers & Profiles choose Certificates > + > Apple Development, and upload a certificate signing request made in Keychain Access (Certificate Assistant > Request a Certificate from a Certificate Authority, saved to disk). Keep the private key that Keychain Access creates on that Mac: the profile in step 3 needs this certificate, and signing needs its private key. Apple's page for the request: [Create a certificate signing request](https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request). The rest of this step is the usual flow and was not checked against an Apple page.
1. **Create the App ID.** Identifiers > + > App IDs > App, choose Explicit and enter your bundle ID. Under Capabilities tick **Declared Age Range**. Apple's page: [Register an App ID](https://developer.apple.com/help/account/identifiers/register-an-app-id). Apple's entitlement reference, for the key the sample's `Entitlements.plist` sets: [`com.apple.developer.declared-age-range`](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.declared-age-range). In this project the capability was set on the App ID in the account and showed up in the installed profile as `com.apple.developer.declared-age-range` (device-test-plan.md section 6.2); check yours the same way.
2. **Register the iPhone.** Devices > +, with a name and the device's UDID (Finder, or Xcode's Devices window, shows it). Apple's page: [Register a single device](https://developer.apple.com/help/account/devices/register-a-single-device).
3. **Create a development provisioning profile** for the App ID: iOS App Development, pick the App ID, your Apple Development certificate and the iPhone, name it (for example `AgeSample Development`), download it and install it by opening the file. Apple's page: [Create a development provisioning profile](https://developer.apple.com/help/account/provisioning-profiles/create-a-development-provisioning-profile) (that page does not describe installing; for manual signing it points to Xcode's help on manually signing an app).
4. **For the UI test only:** register a second, **wildcard** App ID with the bundle ID suffix `com.example.agesample.uitests.*`, and a development profile for it that includes the iPhone. One wildcard has to cover both the host app (`...uitests.host`) and the runner Xcode derives from the test bundle (`...uitests.xctrunner`). Same Apple pages as steps 1 and 3 (the register page describes the Wildcard option).

### Local, untracked files

These are ignored by git (`*.user`, `scripts/sample-id.local`, `scripts/devices.local` and `tests/ios-device-uitests/signing.local` are in `.gitignore`). Do not commit them.

- `samples/AgeledgerSample/AgeledgerSample.csproj.user`: MSBuild imports it automatically. The .NET iOS SDK reads two properties from it, `CodesignKey` and `CodesignProvision` (the sample's csproj only mentions them in a comment):

  ```xml
  <Project>
    <PropertyGroup>
      <CodesignKey><exact identity name printed by: security find-identity -v -p codesigning></CodesignKey>
      <CodesignProvision>PROFILE-UUID-OF-YOUR-SAMPLE-PROFILE</CodesignProvision>
    </PropertyGroup>
  </Project>
  ```

  Use the profile's **UUID**, not its name: a name can match an older profile and then the device install fails (device-test-plan.md section 6.3). `security find-identity -v -p codesigning` lists your signing identities; the UUID is inside the downloaded `.mobileprovision` (for example `security cms -D -i <file>` and look for `UUID`). The same two values can instead be passed on the command line as `-p:CodesignKey=... -p:CodesignProvision=...`. The `Entitlements.plist` is applied to device builds only.
- `scripts/sample-id.local`: your bundle ID (section 2).
- `scripts/devices.local`: copy `scripts/devices.local.example`. One allowed device per line (an Android serial or an iPhone UDID); a device that is not listed is refused. The self-test scripts (`selftest-ios.sh`, `selftest-android.sh`) match the whole line, so the device id must be **alone on its line** for them. `scripts/ios-uitests.sh` also accepts an optional name after the UDID that it scrubs from its output, but with a name on the line `selftest-ios.sh` refuses that device. If you use both, put the id alone.
- `tests/ios-device-uitests/signing.local`: copy `signing.local.example` and set `TEAM_ID=YOUR_TEAM_ID` and `PROFILE_UUID=` to the **UI-test wildcard profile's** UUID (step 4). The script sources this file as shell code, so keep it to those two assignments.

## 4. The Sandbox Apple Account and Developer Mode

The iPhone returns age-range answers for the sample through Apple's sandbox, not a real account.

1. Create a Sandbox Apple Account in App Store Connect, Users and Access > Sandbox: [Create a Sandbox Apple Account](https://developer.apple.com/help/app-store-connect/test-in-app-purchases/create-a-sandbox-apple-account). Apple's page says not to use an address that is already an Apple Account or has been used to buy content. Use a **new address that has never been used for a real Apple Account** (a plus-address on a mailbox you control works, as Apple's page describes). Name, email and password cannot be edited afterwards.
2. On the iPhone turn on Developer Mode: Settings > Privacy & Security > Developer Mode, then restart as asked. Apple's page: [Enabling Developer Mode on a device](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device). Usually the Developer Mode toggle only appears once the phone has been connected to a Mac running Xcode: if it is missing, connect the phone and open Xcode > Window > Devices and Simulators once (or try the install in section 5 first); the toggle then appears. That is the usual behaviour; we have not checked it against Apple's page.
3. Settings > Developer > **Sandbox Apple Account**, sign in with the account from step 1, select it, tap **Manage**, then **Age Assurance**. On iOS 27.0.1 (what this project observed) the same page was reached as Account Settings > Age Assurance. Apple's page on the scenarios: [Testing age assurance in sandbox](https://developer.apple.com/documentation/storekit/testing-age-assurance-in-sandbox). Secondary and unlinked: a forum answer said at one point that the steps on that page were outdated. The path above is the one in this repository's notes (device-test-plan.md section 6.1).
4. Note which scenario is selected now (probably Off), so you can set it back in section 7.

Apple's scenarios are: Under 13 (significant change approved), 13-15, 16-17, 18+ age not confirmed, 18+ age confirmed, and 18+ age confirmed with a significant change applicable, plus Off (device-test-plan.md section 6.4).

## 5. Build, install and run on the iPhone

From the repository root, with your values in `AgeledgerSample.csproj.user`:

```
dotnet build samples/AgeledgerSample -f net10.0-ios -c Debug -p:RuntimeIdentifier=ios-arm64
xcrun devicectl device install app --device <IPHONE_UDID> <path to AgeledgerSample.app>
xcrun devicectl device process launch --device <IPHONE_UDID> com.example.agesample
```

The first iOS build also builds the Swift wrapper (`native/ios/build.sh`, needs Xcode). The `.app` is under the sample's `bin/` folder for that configuration and runtime identifier; for the device build it is `samples/AgeledgerSample/bin/Debug/net10.0-ios/ios-arm64/AgeledgerSample.app`. To check that the entitlement made it into the signed app: `codesign -d --entitlements :- <the .app>` should show `com.apple.developer.declared-age-range`. (The README's "Unverified" list notes that this project has not checked the signing of the embedded framework beyond what its device runs showed. The framework inside the package carries an ad-hoc signature, verified with `codesign` but not yet launched on a Simulator with `-p:EnableCodeSigning=false`; earlier packages without it were killed at launch in that setup.)

In the sample:

1. Turn **Enabled** on (the library is off by default, so Evaluate does nothing useful without it). Pick the gates preset `[13,16,18]` and leave `MinimumAge` at 13.
2. Tap **Evaluate**.
3. Compare with this table (device-test-plan.md section 7.3, rows I3-I7; observed on one iPhone on iOS 27.0.1 in three runs, so yours may differ on another OS version):

| Sandbox scenario (label as shown in Settings on iOS 27.0.1) | State | Bucket | Decision |
|---|---|---|---|
| User 18+, age confirmed, significant change not applicable | Shared | `18+` | Allowed |
| User 18+, age not confirmed, significant change not applicable | Shared | `18+` | Allowed |
| User 18+, age confirmed, significant change applicable | Shared | `18+` | Allowed |
| User 16-17, significant change declined | Shared | `16-17` | AllowedWithParentalConsent |
| User 13-15, significant change approved | Shared | `13-15` | AllowedWithParentalConsent |
| User under 13, significant change approved | Shared | `0-12` | Blocked |

The labels are the ones the UI test matches on iOS 27.0.1 (`tests/ios-device-uitests/AgeledgerUITests/Support.swift`); on other iOS versions they may read differently. Change the scenario in Settings between runs. No consent sheet appeared in the sandbox in this project's runs.

Two things to know:

- With the scenario **Off**, Evaluate did not return within 60 seconds in this project's runs. Use the sample's request timeout picker (for example 15000 ms) or its Cancel button instead of waiting. See the Unverified list in the README for what has and has not been seen.
- On the **iOS Simulator** the real Apple backend returned Unavailable in this project's runs; use an iPhone for real values. The Simulator needs no signing: add `-p:RuntimeIdentifier=iossimulator-arm64`, and `-p:EnableCodeSigning=false` on a Mac with no signing identity.

The Debug build also has a built-in self-test that uses fakes only and never shows Apple's sheet: `scripts/selftest-ios.sh <IPHONE_UDID>` (needs `scripts/devices.local`; see device-test-plan.md section 7.6).

## 6. Optional: the iPhone UI test

`scripts/ios-uitests.sh` drives the sample and Settings on the phone: it reads the current Age Assurance scenario, switches through the scenarios, taps Evaluate for each and compares the sample's bucket and decision with the table above. **It changes a device setting** (the sandbox scenario) and sets it back at the end.

Before the first run: the wildcard App ID and profile (section 3 step 4), `signing.local` and `devices.local` (section 3), the sandbox account signed in, the phone unlocked and plugged in, and Do Not Disturb on.

```
scripts/ios-uitests.sh --udid <IPHONE_UDID> --test ExplorationTests/testExploration --app <path to the Debug AgeledgerSample.app>
```

- The UI test has only been run on an iPhone with Touch ID; Face ID has not been tried.
- The phone may ask for **Touch ID** once or more when Settings opens the sandbox account page, so someone has to hold the phone. The log prints `ACTION NEEDED: touch the Touch ID sensor on the iPhone now`.
- **Do not interrupt it.** If the run fails or stops, the script restores the scenario from a separate run. Only a read-back counts as restored. It prints `SCENARIO NOT RESTORED: <label>` if it could not verify the restore; then check the scenario by hand.
- Restore marker: the script keeps a file `pending-restore-<id>` in `~/Library/Application Support/ageledger-uitests/`. It holds the scenario to restore (or `unknown`). If it says `unknown` the script refuses to start until you have checked the scenario by hand and deleted the file.
- Output goes under `bin/ios-uitests/<stamp>/`; read `main.scrubbed.log`.

All of the details (opt-in rows, the system sheet it stops on, what is recorded) are in device-test-plan.md section 7.7.

## 7. Put things back

- In Settings > Developer > Sandbox Apple Account > Manage > Age Assurance, select the scenario you wrote down in section 4 step 4 (the UI test does this for itself).
- If you want, sign out of the Sandbox Apple Account, and turn Developer Mode off (Settings > Privacy & Security > Developer Mode; the phone restarts).
- Delete the Debug sample from the phone.

## 8. Android

- Any Android phone or emulator. The README states API 23+ and Play Store/Play Services for apps using the library. The Fake scenarios need no account and no Play Console. This project's emulator images were `android-36` `google_apis` (no Play Store), where the real manager only reaches its error paths.
- Build and install a Debug build (with two Android devices attached pass the target explicitly):

  ```
  dotnet build samples/AgeledgerSample -f net10.0-android -c Debug -t:Install -p:AdbTarget="-s <ANDROID_SERIAL>"
  ```

  Needs the `maui` workload and the Android SDK platforms 35/36 and build-tools (device-test-plan.md section 4).
- Self-test: `scripts/selftest-android.sh <ANDROID_SERIAL>` (add `--real-play` to also run the opt-in suite against the real Play service). List the serial in `scripts/devices.local` first. It uses Google's `FakeAgeSignalsManager`, needs no account, and exits 0 only if every row passed. The script stops if the install is a Release build. See device-test-plan.md section 7.6.
- In the app's Debug build a picker offers the Fake scenarios and a "Real manager" option.
- The real Play manager: Google says the API is blocked for "apps that aren't updated through Google Play" (`APP_NOT_OWNED`); license testers can bypass this for a sideloaded debug build if the package name matches an app in your Play Console and the device's Google Account is a license tester: [Test your Play Age Signals API (beta) integration](https://developer.android.com/google/play/age-signals/test-age-signals-api). This project saw `NotShared` for a sideloaded app with no Play Console app. It has **not** seen the real Play manager return a real age range (README, "Unverified"), so this guide does not say what you would see.

## Links checked

Checked 2026-10-27. "Loaded" means the page text was readable; "title only" means the page exists but the tool could read only its title, so the steps above for it come from this repository's own notes.

| Page | Result |
|---|---|
| [Register an App ID](https://developer.apple.com/help/account/identifiers/register-an-app-id) | loaded |
| [Create a certificate signing request](https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request) | loaded |
| [Register a single device](https://developer.apple.com/help/account/devices/register-a-single-device) | loaded |
| [Create a development provisioning profile](https://developer.apple.com/help/account/provisioning-profiles/create-a-development-provisioning-profile) | loaded |
| [Create a Sandbox Apple Account](https://developer.apple.com/help/app-store-connect/test-in-app-purchases/create-a-sandbox-apple-account) | loaded |
| [Enabling Developer Mode on a device](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device) | title only |
| [Testing age assurance in sandbox](https://developer.apple.com/documentation/storekit/testing-age-assurance-in-sandbox) | title only |
| [`com.apple.developer.declared-age-range`](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.declared-age-range) | title only |
| [Test your Play Age Signals API (beta) integration](https://developer.android.com/google/play/age-signals/test-age-signals-api) | loaded |
| [Certificates, Identifiers & Profiles](https://developer.apple.com/account/resources) | not fetched (needs a sign-in) |
