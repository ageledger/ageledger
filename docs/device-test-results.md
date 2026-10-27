# Device test results

Date: 2026-10-25 to 2026-10-27.

Commit hashes and branch names below refer to development history before the public repository; they are not in it.

Builds tested:

- First run: the sample app (Debug, with Google's Fake manager and a toggle for the real Play manager) as it was before the rebase onto main. Its library had no narrower `.tmp` cleanup pattern, no bucket `unknown` fallback for invalid platform labels, and no MinimumAge/AdultAge checks.
- Re-run ("Re-run (fixed sample)" below): commit 0829055 plus the sample fixes in the same change, so the library after the rebase. Debug for the Fake rows; Release with the real Play manager for 8.5(a).
Test plan: [device-test-plan.md](device-test-plan.md).

## Android phone, Android 13 (API 33), first run

The OS version was read from the device (`getprop`).

Defaults: Enabled = true, age gates [13,16,18], MinimumAge 13, rule id TEST, bucket logging Exact. The result column shows state / bucket / decision / Logged.

| Row | Observation | Result |
|---|---|---|
| A1 | Shared, lower 18: Shared / 18+ / Allowed / true | PASS |
| A2 | Shared 16-17: AllowedWithParentalConsent | PASS |
| A3 | Shared 13-15: AllowedWithParentalConsent | PASS |
| A4 | Shared 0-12: Blocked | PASS |
| A4b | Shared 0-17: Blocked | PASS |
| A4c | A4b with MinimumAge 18: Blocked | PASS |
| A4d | Shared 12-17: Blocked | PASS |
| A5 | Shared, no bounds: NotShared / unknown / NotApplicable | PASS |
| A6 | NotShared: NotShared / unknown / NotApplicable | PASS |
| A7 | VerificationRequired: Blocked (the Fake accepts it) | PASS |
| A8 | 13-15 with significant change Pending: reported, AllowedWithParentalConsent | PASS |
| A9 | 18+ with significant change Approved: reported, Allowed | PASS |
| A10 | NetworkError on the access call and on the check call: Error / Network, NotApplicable, Logged | PASS |
| A11 | ApiNotAvailable on both calls: Unavailable / NotAvailable | PASS |
| A12 | AppNotOwned on both calls: Unavailable / AppNotOwned | PASS |
| A13 | PlayStoreNotFound, PlayServicesNotFound, CannotBindToService, PlayStoreVersionOutdated, PlayServicesVersionOutdated, SdkVersionOutdated: each Unavailable / PlatformComponentMissing | PASS |
| A14 | ClientTransientError: Error / Transient; InternalError: Error / Internal | PASS |
| A15 | Custom policy (always Blocked) with A1: Blocked | PASS |
| A16 | No activity: Error / Other / unknown / NotApplicable, Logged. The first scripted attempt produced no result (test driver issue, no log entry); a manual retry passed | PASS |
| A20 | Real manager on a sideloaded app: Android / NotShared / unknown, no error, NotApplicable, Logged, 526 ms, one entry. The Play Store service was bound at this first Evaluate | DIFFERENT (plan expected Unavailable / AppNotOwned) |
| A21 | Needs an emulator | NOT RUN |
| A22 | Licence tester, deferred | NOT RUN |
| A23 | Needs a Play Store setting change | NOT RUN |
| L1 / L1b | Export is a JSON array with exactly the five fields; timestamps end in Z; bucket `18+` is literal | PASS |
| L2 / L2b | Clear removes the log and leftover `.tmp` files; an unrelated file is untouched; export gives `[]` | PASS |
| L3 | Persistence across force-stop and relaunch: the log had been cleared before the relaunches | NOT RUN (run in the re-run) |
| L4 | Appending 1001 entries kept exactly 1000 valid lines | PASS |
| L5 | Enabled = false: Disabled / NotApplicable, Logged = false, no bind, no log entry | PASS |
| L6 | Two simultaneous Evaluate taps: two entries, log valid | PASS |
| L7 | Unwritable log path: result and decision shown, Logged = false, no crash, no exception text; Logged = true after restoring | PASS |
| L8 / L9 | Invalid settings (gates and an empty rule id) refused with ArgumentException when enabled; accepted when disabled; valid gates built. MinimumAge -1 was accepted at the time (now rejected by the library, see below) | PASS |
| L10 / L11 / L12 | Unknown bucket logging stores `unknown` while the screen shows the range; decisions unchanged (option since renamed Withheld; it now logs `withheld`) | PASS |
| 8.1 N=0 | `TaskCanceledException` (an `OperationCanceledException`), 2 ms, no log entry | PASS |
| 8.1 N=2000 | The call finished before the token fired (about 30 ms), so cancellation during a wait cannot be exercised with the Fake | PASS (limited) |
| 8.2 | "Evaluate on a background thread" about 6 times: normal results, no crash (not the full 20 repeats) | PASS |
| 8.3 | Evaluate at launch gave Error / Other in both delays, but that came from the unconfigured Fake after relaunch, not from the activity | DIFFERENT (inconclusive; sample fixed, see the re-run) |
| 8.4 | Every one of the 11 codes: the Fake throws exactly `AgeSignalsException` (no wrapper, no inner type); the library maps each to its own category, never Other | PASS |
| 8.5(a) | Needs a Release build | NOT RUN (run in the re-run) |
| 8.5(b) | Debug + Fake, Enabled off then Evaluate: no service bound, no Play log lines, no result, no entry | PASS |
| I11b | The mutate button was overwritten by the rebuild at Evaluate, so mutation after build could not be tested (sample fixed afterwards) | DIFFERENT |

## Android phone, Android 13 (API 33), re-run (fixed sample)

Fresh install, Enabled off by default. The Fake scenario was changed to Shared 16-17 (not the 13-15 default) so a restore can be told apart.

| Row | Observation | Result |
|---|---|---|
| Setup | Enabled on, Shared 16-17, Apply, Evaluate three times: Shared / 16-17 / AllowedWithParentalConsent / Logged = true; export has 3 entries | PASS |
| L1 / L10 | Exact logs `16-17`; AgeBucketLogging Unknown logs `unknown`; decision AllowedWithParentalConsent in both (option since renamed Withheld; it now logs `withheld`) | PASS (post-rebase library) |
| 8.3 delay 0 | Evaluate at launch, force-stop, relaunch: with no manual action the launch result was Shared / 16-17 / AllowedWithParentalConsent / Logged = true (about 1.3 s), one new log entry. The scenario is restored at launch | PASS |
| 8.3 delay 3000 | No result right after launch; the result appeared after the delay (same values), one new entry. Timing observed coarsely. The first attempt did not save the delay because the field was left with Back; the sample now also saves it when the page disappears and before the launch evaluation | PASS |
| L3 | Log persisted across 4 relaunches (3, 4, 5, 6 entries, never dropped) | PASS |
| 2a | After relaunch with no Apply, the launch evaluation used the previously applied scenario (16-17); the fields show the restored values | PASS |
| 2c | MinimumAge 120, Evaluate: "Service not rebuilt: ArgumentException", "Not evaluated: invalid settings", export count unchanged. An invalid gate preset was not tried in this run | PASS |
| L2b | With the log and two leftover `.tmp` files present, Clear left only the unrelated file; export `[]` | PASS (post-rebase library) |
| 8.5(a) | Release build, real Play manager, Enabled off: result Disabled / NotApplicable / Logged = false, no PlayCore or AgeSignalsService lines in logcat, so no bind | PASS |
| 8.5(a), Enabled on | The first bind appeared at the first Evaluate with Enabled on (PlayCore bound to the Play Store, then unbind); result Android / NotShared / unknown, no error, NotApplicable, Logged = true, 152 ms | PASS |

## Notes

- A20 (plan expected Unavailable / AppNotOwned): the real Play manager on a sideloaded app bound to the Play Store and returned NotShared with no error, not an AppNotOwned-type error. The success path with a real age signal (licence tester, supported state) is still unverified.
- 8.1 N=0: the call throws `TaskCanceledException`, which is an `OperationCanceledException`, and writes no log entry.
- The Fake's queued values are sticky: an Evaluate without a new Apply returns the previous scenario again.
- Error mapping: every code maps to its own category, and the Fake throws exactly `AgeSignalsException` with no wrapper, so the README's `AsAsync` question is answered for the Fake. It is not answered for the real manager's error path.
- L4: the 1000-entry cap holds.
- L7: Logged = false with no crash.
- Observed in the run and changed afterwards: Evaluate after a refused rebuild used the previous valid service (the sample now refuses to evaluate), and the library accepted MinimumAge -1 (it now rejects MinimumAge or AdultAge outside 0..99 and AdultAge below MinimumAge when enabled).

## Not run

- A21: needs an emulator.
- A22: deferred.
- I11b (mutation after build): obsolete; the service now copies its options at construction and the sample's mutate control was removed.
- A23: needs a Play Store setting change.

## iOS Simulator (headless)

These results predate the sample's adoption of the UIScene lifecycle and have not been re-run with it. iOS 26.5 Simulator, iPhone device type, booted headless (no Simulator.app). Build from HEAD ec1fca5 (`net10.0-ios`, Debug, `iossimulator-arm64`); the bundle contains the native framework under `Frameworks/` and the app has no `NativeReference` of its own. The settings (`enabled`, `evaluate-at-launch`, `evaluate-at-launch-delay-ms`) were written to the app's preferences after the first run.

| Row | Observation | Result |
|---|---|---|
| Launch, Enabled off | The app links and launches with the native framework, with no dyld or link error. This confirms the binding fix on a running app. No platform call (no Apple age-range log lines) and no log file | PASS |
| Enabled + evaluate at launch, delay 0 | The library found a presenter and called Apple's `requestAgeRange` (system log: `AgeRangeController requestAgeRange`, remote flow presented, dismissed by the system about 90 ms later). No sheet visible, no crash. One log entry: ageBucket `unknown`, decision `NotApplicable` | PASS (defined state, one entry) |
| Enabled + evaluate at launch, delay 3000 ms | Same sequence about 3 s after launch, same entry (`unknown` / `NotApplicable`), no sheet, no crash | PASS |

- `NoPresenter` did not occur at either delay.
- Crash check: the log scan found no exception, crash, dyld or fatal lines.
- Limits: the on-screen result (state and error) could not be read headlessly because the page is clipped, so the library's exact state for the Simulator is not recorded; the `unknown` / `NotApplicable` values come from the exported log. Button-driven rows (cancel-after, background-thread evaluate, debug buttons, export), answering a sheet, the Main Thread Checker, backgrounding and a second Evaluate while a sheet is up were not run on the Simulator.

## iPhone

Age rows: run on 2026-10-26 with the UI test, see "iPhone UI-test runs, 2026-10-26" below.

Launch only (2026-10-25, iPhone running iOS 27.0.1, Debug build with the iOS 27.1 SDK): the earlier sample build, without scene lifecycle adoption, crashed at launch with UIKit's no-scene-lifecycle runtime issue. The build that adopts the UIScene lifecycle launched and stayed running for 30 s with no crash report. No age rows were run.

## Self-test runs (Debug sample), 2026-10-25

The Debug sample's built-in self-test (device-test-plan.md, sections 7.5 and 7.6), started by the host scripts (this version, including the per-case count check) with the default suites and without `--real-play`. The fake manager (Android) and fake backend (iOS) supply every range; the `real-play` suite (A20/A21) was not run, and no Apple sheet can appear in these runs.

| Device | Main suite | Restart check (L3) | Exit code | Time |
|---|---|---|---|---|
| Android 13 phone | 57 of 57 cases passed, complete, no case-count mismatch | write 1 of 1, read 1 of 1 | 0 | about 93 s |
| iPhone on iOS 27.0.1 | 20 of 20 cases passed (including L13), complete, no case-count mismatch | write 1 of 1, read 1 of 1 | 0 | about 73 s |

Nothing moves out of the README "Unverified" list because of these runs: they test the library's logic and, on Android, the binding code against fakes, not Apple's sheet or Google's live service.

## iPhone UI-test runs, 2026-10-26

An iPhone running iOS 27.0.1 with an Apple sandbox account, the Debug sample, `Enabled` on, gates `[13,16,18]`, `MinimumAge` 13, driven by the UI test in `tests/ios-device-uitests` through `scripts/ios-uitests.sh` (device-test-plan.md, section 7.7). The test switched Settings > Developer > Sandbox Apple Account > Age Assurance between scenarios, relaunched the sample, tapped Evaluate and read the result label.

**The runs.** Twelve runs were made on 2026-10-25 and 2026-10-26; runs 11 and 12 finished normally.

- Runs 1-9 stopped while navigating Settings (an automation permission prompt, an account action sheet, a Touch ID prompt, a screen that was not the expected one). They never changed the scenario, so the phone stayed on its starting scenario, Off.
- Run 10 switched to "User 18+, age confirmed, significant change not applicable" and could not set Off back: the Off row was not found by its label (a lookup bug), and the Touch ID prompt that came up when Settings was opened again was not answered within the 120 second wait because nobody was at the phone. The phone was left on that scenario.
- Run 11 (2026-10-26) started by restoring the scenario that run 10 had left, read back Off, ran the full flow, and restored Off with a read-back of the selection. Exit 0. The table below is from that run.

| Sandbox scenario | State | Bucket | Decision | ParentalControls | Error | SignificantChange | Logged |
|---|---|---|---|---|---|---|---|
| 18+, age confirmed, significant change not applicable | Shared | `18+` | Allowed | None | None | None | True |
| 18+, age not confirmed, significant change not applicable | Shared | `18+` | Allowed | None | None | None | True |
| 18+, age confirmed, significant change applicable | Shared | `18+` | Allowed | SignificantAppChangeApprovalRequired | None | None | True |
| 16-17, significant change declined | Shared | `16-17` | AllowedWithParentalConsent | SignificantAppChangeApprovalRequired | None | None | True |
| 13-15, significant change approved | Shared | `13-15` | AllowedWithParentalConsent | SignificantAppChangeApprovalRequired | None | None | True |
| Under 13, significant change approved | Shared | `0-12` | Blocked | SignificantAppChangeApprovalRequired | None | None | True |
| Off | not recorded: the result label stayed empty in that run | | | | | | |

- **No consent sheet appeared.** In this sandbox setup Apple returned a range straight away for every scenario, including the first Evaluate after the scenario was chosen. The decline path (I8), a remembered answer, and cancelling while a sheet is up were therefore not exercised.
- **Touch ID.** Settings asked for Touch ID when the sandbox account page was opened. A run needs at least one touch on the phone, for each fresh Settings session; the test waits up to 120 seconds and never taps Cancel. Moving to the sample and back could lock the page again, so one touch per run is not guaranteed.
- **How the selected scenario shows.** The Age Assurance page has no checkmark and no selected trait. After a scenario is chosen, its row's container reports the value `1` and the other rows `0`; the Off row is one of the rows. The restore to Off in run 11 was read back from the row values. On iOS 27.0.1 run 14 showed that the Off row itself reads `1` when Off is selected; the rule "all six long rows read `0` means Off" has not been seen on a device.
- **Off.** With the sandbox scenario Off, Evaluate did not return within 60 seconds (run 11 recorded no state for it). Whether the library's timeout or cancellation covers this is not verified (see the 2026-10-27 I18 row).
- **The per-row comparison.** In run 11 the comparison with the expected bucket and decision flagged every row as a mismatch although the values matched: the label's line breaks came back as spaces and the parser split on lines. The table above was read from the recorded labels, not from that comparison.

### Run 12 (2026-10-26, after the identifier rename)

The same iPhone (iOS 27.0.1, sandbox account) with the renamed sample and UI-test bundle ids, started with the person who holds the phone present. Exit 0.

- The starting scenario (Off) was read from the page, not assumed. One Touch ID touch was needed. Every scenario switch was read back.
- Under 13 gave `Shared`, `0-12`, `Blocked`; 13-15 and 16-17 gave `Shared`, `AllowedWithParentalConsent`; all three 18+ scenarios gave `Shared`, `18+`, `Allowed`. Every row compared as a match against the plan's expected values.
- The first and second Evaluate showed no consent sheet in the sample, so whether a declined answer is remembered is not meaningful here.
- The Off row again had no result label after 60 seconds (the sample's status line read "Service built"). While it was missing, a system sheet titled "Age-Appropriate Experiences in Apps" (buttons Continue and Update Birthday, with a close control) appeared in Settings around that row. Nobody was looking at the phone and nothing was tapped, by the person or by the runner: the sheet stayed unanswered and went away when the runner closed Settings for the next step. The missing Off result is consistent with the app waiting for that unanswered sheet; this is not confirmed.
- Settings was re-navigated twice ("session lost") without a new Touch ID prompt, and the final restore to Off was confirmed by reading the selection back.
- One screenshot taken for the empty Off result was found to show that system sheet in Settings, because the foreground check let it through; it was deleted. The check is stricter now (below).

**Run on a device in run 12:** the restore rules (including reading the starting scenario from the page), the recording rules, `ResultParser`, the 60 second wait, and the renamed bundle ids and profiles.

**Changed after run 12** (items marked "run on a device in runs 13 and 14" were exercised there; the rest have not been run on a device yet):

- Off is recognised by the Off row's own value `1` (seen in run 14) or by all six long rows being present and reading `0` (not seen on a device); otherwise the scenario is unreadable. Only the Off-row reading ran on a device, in run 14.
- Selecting a scenario that already reads back as selected taps nothing.
- The restore marker file ("unknown", then the label; removed when the test reports that it changed nothing) lives outside the repository and outside `/tmp`. Run on a device: in run 13 "unknown" was written at the new location and the did-not-start path removed it; in run 14 "unknown" was replaced by the label and removed after the read-back restore. The `NOTHING-CHANGED` path, the refusal to start when the file still says "unknown" (exit 7) and skip-when-already-selected have not been run on a device.
- A Touch ID prompt that is still up after the wait stops the run without tapping.
- Apple's "Age-Appropriate Experiences in Apps" sheet is handled as a known system sheet: its labels are recorded, nothing is tapped, Settings is closed, the remaining rows are stopped, and the scenario is restored with a read-back.
- The screenshot guard: a screenshot of the sample is taken only when the sample is in the foreground, Settings is not, and neither Settings nor SpringBoard shows an alert or sheet, checked before and after the capture. No screenshot is taken for an empty result; the notes record which of the sample, Settings and SpringBoard were in the foreground and any system sheet's known labels.
- Share buttons are matched by exact label, and a consent-sheet button is tapped only inside a container that mentions an age range when looked at again just before the tap. No consent sheet has appeared in the sample, so this gate has never been seen against a real sheet.
- The verdict rule: rows are "recorded" when no sheet appeared (so nothing could be remembered) and the switch read back (run on a device in run 14).
- The script's output also drops the team id and the signing lines, and honours `AGELEDGER_LOCK_DIR`.

Nothing moves out of the README "Unverified" list except the sandbox rows named in the plan (section 7.3): one device, one OS version, three runs (11, 12 and 14).

### Run 13 and 14 (2026-10-27)

**Run 13.** UI automation timed out at the start (cause not confirmed; the phone may not have been ready). The test did not start, the script reported that nothing was changed, and no restore marker was left: "unknown" was written at the new location and the did-not-start path removed it.

**Run 14.** The merged code on an iPhone running iOS 27.0.1 with an Apple sandbox account, with the person holding the phone present. Exit 0.

- One Touch ID touch was needed, and one Settings session covered the whole run.
- The starting scenario (Off) was read from the page: it was recognised by the Off row's own value `1` (the trail shows the scenario rows read with zeroRows=0 of 6). The `ORIGINAL-SCENARIO` line was streamed live into the log.
- No consent sheet appeared. All six scenarios returned `Shared`, no error and logged `True`. Under 13 gave `0-12` / `Blocked`; 13-15 and 16-17 gave `AllowedWithParentalConsent`; all three 18+ scenarios gave `18+` / `Allowed`. Every row compared as a match and its verdict was "recorded" (the verdict rule). The first 18+ scenario covers the first and second Evaluate; the other five are scenario rows.
- Every scenario switch was read back (including Off, twice), and the final restore to Off was confirmed by reading the selection back. No "already selected" skip appeared. No screenshot was kept (none for the empty Off result either) and no restore marker was left.
- **Off.** For the third time Evaluate returned nothing within 60 seconds. This time no system sheet appeared and the sample was in the foreground. The cause is not confirmed (library, sample or the platform call); whether the library's `RequestTimeout` covers this is open (row I18). The Off row's verdict was unknown (not checked).

**Exercised on a device by runs 13 and 14:** the Off reading through the Off row's value, the "unknown" marker and the did-not-start path (run 13), the label replacing "unknown" and the removal after the restore (run 14), the verdict rule, and the new location of the pending-restore file.

**Not yet exercised:** the all-six-zeros Off reading, `NOTHING-CHANGED`, the exit-7 refusal, skip-when-already-selected, the authorisation-timeout throw, the handling of Apple's system age sheet, and the screenshot guard (no sheet appeared, so nothing tested it).

## Runs on 2026-10-27

Builds: the Simulator and Android phone runs are from main at commit abaa080. The iPhone six-scenario run and the I18 run were both made from branch feat/i18-off-timeout at 1808a9b (since merged into main), which includes the internal binding and the new native slices; the UI-test code was that commit and the sample .app was built from the same branch. No device identifiers are recorded here.

| Target | Observation | Result |
|---|---|---|
| iOS Simulator, iOS 27.0 (headless), main abaa080 | The app launched and the self-test passed 20/20. The real Apple backend, through the internal binding, returned Unavailable / NotAvailable and logged it. No crash, and no registrar or marshalling errors in the log | PASS |
| iPhone, iOS 27.0.1, Apple sandbox account, UI test, 1808a9b | All six age scenarios returned `Shared` with no error, logged, and matched the expected bucket and decision: 18+ (three scenarios) Allowed; 16-17 and 13-15 AllowedWithParentalConsent; under 13 `0-12` Blocked. The Off row gave no result, as in earlier runs | PASS (six age scenarios) |
| iPhone, iOS 27.0.1, sandbox, I18 (Off, `RequestTimeout` 15 s), 1808a9b | A separate run after the one above. Its original scenario was read as Off. In this one run, the timeout ended the wait at about 15.1 s (State Error, Error Timeout; one iPhone, iOS 27.0.1, sandbox). Its own restore read back Off | observed once |
| iPhone, I18 cancellation pass | Not exercised: the Cancel button was off screen on the small display and the test never scrolls. The button has since been moved beside Evaluate; that change has not been run | not run |
| Android phone, Android 13, main abaa080 | Self-test all 57/57; restart pair 1/1 and 1/1. The real Play manager returned NotShared with no error and logged it. No crashes | PASS |

iPhone notes:

- Navigation needed no Touch ID: Manage went straight to Account Settings.
- Two earlier attempts of the six-scenario run timed out waiting on an authentication element that was not on screen. The test now waits only for a visible prompt, then for the Account Settings and Age Assurance markers.
- The six-scenario run's end-of-run restore hit a real Touch ID prompt that was not answered in time; the restore failed without tapping anything, and the scenario was set back to Off by hand.
- The I18 run was a separate run afterwards; its restore needed no hand reset and read back Off.
