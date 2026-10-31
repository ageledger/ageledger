#!/usr/bin/env bash
# Runs one UI test class or test from tests/ios-device-uitests on a physical iPhone.
#
#   scripts/ios-uitests.sh --udid <udid> --test <Class[/test]> [--app <path to the sample .app>] [--lock-timeout <seconds>]
#                          [--assume-original-off]
#   scripts/ios-uitests.sh --udid <udid> --row i18 [--app <path>] ...     (opt-in row I18: sandbox Off, request timeout, then Cancel)
#   scripts/ios-uitests.sh --udid <udid> --reinstall-probe --app <path to the sample .app>
#
# The sample must already be installed on the device; with --app it is installed first (preferred). --reinstall-probe
# uninstalls ONLY the sample (scripts/lib/sample-id.sh), installs it again from --app, then runs ReinstallProbeTests (the test turns
# Enabled back on, because a reinstall wipes the app's preferences).
#
# What it does: checks the device allowlist and the lock, runs the requested test (which reads the current Age Assurance
# scenario first, attaches it as "original-scenario", and restores it in its own teardown), then reads the original scenario and
# the "restored" flag from the run's attachments. If the run failed, was interrupted, or did not report a restore, the script
# restores the original scenario from a separate run (RestoreTests/testRestore) and prints "SCENARIO NOT RESTORED: <label>" if that
# does not finish and verify. Only a read-back counts as verified: the pending-restore file is deleted only then. The test prints "ORIGINAL-SCENARIO: <label>" as soon as it has read the scenario (before any
# change); the script's live log stream writes the pending-restore file (see below) at that moment, so even a hard kill of the
# script leaves the label for the next run. If that line never appears the original is unknown: check the scenario by hand.
# On iOS 27.0.1 the Age Assurance page shows the selected scenario as a row value (1 for the selected row, 0 for the others; all 0
# means Off), so the starting scenario is read from the page and a restore is confirmed by reading it back. --assume-original-off is
# only a fallback for an OS version without that signal: when nothing reads as selected, the starting scenario is taken to be Off
# (passed to the test as TEST_RUNNER_ASSUME_ORIGINAL=Off). Without the flag an unreadable starting scenario stops the run before any
# change. Each row's result is compared with the expected range in the test report.
# The phone may ask for Touch ID once, when Settings opens the sandbox account: touch the sensor when the log shows
# "ACTION NEEDED: touch the Touch ID sensor on the iPhone now (120 s)" (the test waits up to 120 s and never taps Cancel). If the
# line does not show live, watch the phone. A failure-path restore from outside may ask once more.
# A run takes several minutes: start long runs in the background and read the output file afterwards.
#
# Needs two untracked files:
#   scripts/devices.local                      one allowed device per line: "<udid> [name to scrub from output]"; # starts a comment
#   tests/ios-device-uitests/signing.local     TEAM_ID=... and PROFILE_UUID=... (see signing.local.example). It is SOURCED as
#                                              shell code, so keep it to those two assignments and treat it as trusted.
# Environment: AGELEDGER_LOCK_DIR (then DEVICE_LOCK_DIR, then /tmp/device-locks) holds the per-device locks shared with other users of
# the device, and the pending-restore file below. The name in scripts/devices.local is only used to scrub output, so it has to match
# what the device reports about itself.
#
# --reinstall-probe wipes the sample's preferences and its decision log on the phone (made-up test data only).
#
# Output goes under bin/ios-uitests/<stamp>/. Read only main.scrubbed.log, restore.scrubbed.log and the
# allowed attachments in <name>.attachments/. Raw logs, result bundles and the attachment manifest are deleted after use.
# ~/Library/Application Support/ageledger-uitests/pending-restore-<id> (outside the repository and outside /tmp, so neither a clean
# checkout nor a reboot deletes it) holds "unknown" from just before
# the test starts, then the scenario to restore from the moment the test has read it, until a restore has been verified (by the
# test's own read-back, reported as restored = read-back, or by the outside RestoreTests). The next run restores a label first;
# if the file says "unknown" it refuses to start until the scenario has been checked by hand and the file deleted.
# The ORIGINAL-SCENARIO line was seen live in the log in the device runs so far; the "unknown" marker covers a run where it
# does not arrive.
# Signals: INT, TERM and HUP end the script after the restore step; TERM waits for xcodebuild to finish first, and a second Ctrl-C
# while the restore runs does nothing.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="$ROOT/tests/ios-device-uitests"
DEVICES_FILE="$ROOT/scripts/devices.local"
SIGNING_FILE="$PROJECT_DIR/signing.local"
LOCK_DIR="${AGELEDGER_LOCK_DIR:-${DEVICE_LOCK_DIR:-/tmp/device-locks}}"
WHO="ageledger-uitests"
# shellcheck source=lib/sample-id.sh
source "$ROOT/scripts/lib/sample-id.sh"
SAMPLE_BUNDLE="$SAMPLE_ID"

udid=""; test_id=""; app_path=""; lock_timeout=600; reinstall=0; assume_off=0; row_test=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --udid) udid="${2:-}"; shift 2 ;;
    --test) test_id="${2:-}"; shift 2 ;;
    --app) app_path="${2:-}"; shift 2 ;;
    --lock-timeout) lock_timeout="${2:-}"; shift 2 ;;
    --reinstall-probe) reinstall=1; shift ;;
    --assume-original-off) assume_off=1; shift ;;
    --row)
      # Opt-in rows that are not part of the default run. i18 = device test plan row I18 (Off: request timeout, then Cancel).
      case "${2:-}" in
        i18) row_test="I18Tests/testI18" ;;
        *) echo "--row must be: i18" >&2; exit 2 ;;
      esac
      shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [[ -n "$row_test" ]]; then
  [[ -z "$test_id" ]] || { echo "use either --row or --test, not both" >&2; exit 2; }
  test_id="$row_test"
fi
[[ "$lock_timeout" =~ ^[0-9]+$ ]] || { echo "--lock-timeout must be a number of seconds" >&2; exit 2; }
if [[ $reinstall -eq 1 ]]; then
  [[ -n "$app_path" ]] || { echo "--reinstall-probe needs --app" >&2; exit 2; }
  test_id="ReinstallProbeTests/testReinstallProbe"
fi
[[ -n "$udid" && -n "$test_id" ]] || { echo "usage: $0 --udid <udid> (--test <Class[/test]> | --row i18) [--app <path>] [--lock-timeout <seconds>]" >&2; exit 2; }
[[ -z "$app_path" || -d "$app_path" ]] || { echo "--app must be an .app directory" >&2; exit 2; }

# Allowlist: only devices listed in the untracked file.
[[ -f "$DEVICES_FILE" ]] || { echo "missing scripts/devices.local (allowed devices)" >&2; exit 2; }
device_name=""; allowed=0
while read -r id name || [[ -n "${id:-}" ]]; do
  [[ -z "${id:-}" || "$id" == \#* ]] && continue
  if [[ "$id" == "$udid" ]]; then allowed=1; device_name="${name:-}"; fi
done < "$DEVICES_FILE"
[[ $allowed -eq 1 ]] || { echo "device is not on the allowlist; refusing" >&2; exit 2; }

[[ -f "$SIGNING_FILE" ]] || { echo "missing tests/ios-device-uitests/signing.local (see signing.local.example)" >&2; exit 2; }
# shellcheck disable=SC1090
source "$SIGNING_FILE"
[[ -n "${TEAM_ID:-}" && -n "${PROFILE_UUID:-}" ]] || { echo "signing.local must set TEAM_ID and PROFILE_UUID" >&2; exit 2; }

# Removes the device identifiers from text on stdin: "@" words, the UDID, the device name, UDID-shaped strings.
scrub() {
  local text; text="$(grep -v -e 'Signing Identity:' -e 'Provisioning Profile:' || true)"
  text="${text//"$TEAM_ID"/<team>}"
  text="${text//"$udid"/<device>}"
  if [[ -n "$device_name" ]]; then text="${text//"$device_name"/<device>}"; fi
  printf '%s\n' "$text" | sed -e 's/[^[:space:]"]*@[^[:space:]"]*/<removed>/g' -e 's/00008[0-9A-Fa-f]\{3\}-[0-9A-Fa-f]\{16\}/<device>/g'
}

# Streaming version of scrub for live output (line buffered); same replacements.
scrub_stream() {
  local esc args
  esc() { printf '%s' "$1" | sed 's/[][\.*^$/]/\\&/g'; }
  args=(-l -e '/Signing Identity:/d' -e '/Provisioning Profile:/d' -e "s/$(esc "$TEAM_ID")/<team>/g" -e 's/[^[:space:]"]*@[^[:space:]"]*/<removed>/g' -e 's/00008[0-9A-Fa-f]\{3\}-[0-9A-Fa-f]\{16\}/<device>/g' -e "s/$(esc "$udid")/<device>/g")
  if [[ -n "$device_name" ]]; then args+=(-e "s/$(esc "$device_name")/<device>/g"); fi
  sed "${args[@]}"
}

# Passes lines through and, the moment the test prints "ORIGINAL-SCENARIO: <label>", writes the pending-restore file. Runs as a
# stage of the xcodebuild pipeline, so it only communicates through that file.
watch_lines() {
  local line label
  while IFS= read -r line; do
    printf '%s\n' "$line"
    case "$line" in
      *"ORIGINAL-SCENARIO: "*)
        label="${line#*ORIGINAL-SCENARIO: }"; label="${label%$'\r'}"
        if [[ ( "$label" == Off || "$label" == User* ) && ${#label} -lt 100 ]]; then printf '%s\n' "$label" > "$pending"; fi ;;
      *"NOTHING-CHANGED"*)
        printf 'nothing\n' > "$pending" ;;   # the test threw before it changed anything; finalize_main drops the file
      *"SYSTEM-AGE-SHEET"*)
        printf '%s\n' "A system age sheet appeared on the phone; leave it unanswered (the test stops its rows, restores the scenario, and the sheet goes away)." ;;
    esac
  done
}

# True when an xcodebuild test for this device is running.
other_test_running() {
  ps -axo command= | awk -v u="$udid" '$1 ~ /(^|\/)xcodebuild$/ && index($0, " test") && index($0, u) { f = 1 } END { exit !f }'
}

held=0; need_restore=0; original=""; main_started=0; main_done=0; main_status=0
lock="$LOCK_DIR/$udid"
release() {
  # Only a lock this process holds is ever removed.
  if [[ $held -eq 1 ]]; then rm -f "$lock/owner" && rmdir "$lock" 2>/dev/null || true; held=0; fi
}

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
out="$ROOT/bin/ios-uitests/$stamp"
dd_path="$out/derived-data"
mkdir -p "$out"
# The pending-restore file lives outside the repository and outside /tmp (which is cleared on reboot); the locks stay in LOCK_DIR.
STATE_DIR="$HOME/Library/Application Support/ageledger-uitests"
mkdir -p "$LOCK_DIR" "$STATE_DIR"
pending="$STATE_DIR/pending-restore-$(printf '%s' "$udid" | shasum | cut -c1-8)"
XENV=()

# run_xcb <test> <name> : one xcodebuild test run; result in $out/<name>.xcresult, scrubbed log in $out/<name>.scrubbed.log
run_xcb() {
  local test="$1" name="$2" rc
  if other_test_running; then echo "an xcodebuild test for this device is already running; refusing" >&2; return 3; fi
  set +e
  env ${XENV[@]+"${XENV[@]}"} xcodebuild test \
    -project "$PROJECT_DIR/AgeledgerUITests.xcodeproj" \
    -scheme AgeledgerUITests \
    -destination "id=$udid" \
    -only-testing:"AgeledgerUITests/$test" \
    -derivedDataPath "$dd_path" \
    -collect-test-diagnostics never \
    -resultBundlePath "$out/$name.xcresult" \
    AGELEDGER_SAMPLE_ID="$SAMPLE_ID" \
    CODE_SIGN_STYLE=Manual \
    CODE_SIGN_IDENTITY="Apple Development" \
    DEVELOPMENT_TEAM="$TEAM_ID" \
    PROVISIONING_PROFILE_SPECIFIER="$PROFILE_UUID" \
    2>&1 | scrub_stream | tee "$out/$name.scrubbed.log" | grep --line-buffered -E 'ACTION NEEDED|ORIGINAL-SCENARIO|NOTHING-CHANGED|SYSTEM-AGE-SHEET|Test Case|Test Suite' | watch_lines
  rc=${PIPESTATUS[0]}
  set -e
  rm -rf "$dd_path/Logs"
  if [[ $rc -ne 0 ]]; then
    echo "--- tail of $name.scrubbed.log ---"
    tail -n 30 "$out/$name.scrubbed.log"
  fi
  return $rc
}

PY='
import json, os, re, sys
mode, root, udid = sys.argv[1:4]
devname = sys.argv[4] if len(sys.argv) > 4 else ""
ALLOW = ("exploration-report", "i18-report", "sheet-findings-first", "sample-with-sheet", "original-scenario", "restore-report", "reinstall-probe", "settings-alert", "settings-structure", "sample-tree")
manifest = os.path.join(root, "manifest.json")
kept = []
if os.path.exists(manifest):
    for test in json.load(open(manifest)):
        for a in test.get("attachments", []):
            f = a.get("exportedFileName", "")
            name = a.get("suggestedHumanReadableName", f)
            path = os.path.join(root, f)
            if name.startswith(ALLOW) and os.path.isfile(path):
                kept.append((name, path))
keep = {p for _, p in kept} | {manifest}
for f in os.listdir(root):                      # everything else (system screenshots, hierarchy dumps, ...) is deleted
    p = os.path.join(root, f)
    if p not in keep and os.path.isfile(p):
        os.remove(p)
bad = False
scan = list(kept) + ([("manifest", manifest)] if os.path.exists(manifest) else [])
for name, path in scan:
    is_manifest = path == manifest                         # what remains must hold no "@" and no device identifier
    if path.lower().endswith((".png", ".jpg", ".jpeg", ".heic")):
        continue
    text = open(path, errors="ignore").read()
    if "@" in text or (udid and udid in text) or (devname and devname in text) or re.search(r"00008[0-9A-Fa-f]{3}-[0-9A-Fa-f]{16}", text):
        if is_manifest:                         # always deleted below; not a failure
            continue
        print("SENSITIVE TEXT IN ATTACHMENT, removed: " + name)
        os.remove(path)
        bad = True
def load(prefix):
    for name, path in kept:
        if name.startswith(prefix) and os.path.exists(path):
            try:
                return json.load(open(path))
            except Exception:
                return None
if mode == "original":
    d = load("original-scenario")
    print((d or {}).get("scenario") or "")
else:
    if not kept:
        print("no allowed attachments exported")
    for key in ("exploration-report", "i18-report", "reinstall-probe"):
        d = load(key)
        if not isinstance(d, dict):
            continue
        print(key + ": original scenario =", d.get("originalScenario"), "| unreadable =", d.get("originalUnreadable", False),
              "| original assumed =", d.get("originalAssumed", False), "| restored =", d.get("restored"),
              "| decline remembered =", d.get("declineRemembered"), "| stopped by =", d.get("stoppedBy"),
              *(["| sample timeout reset =", d["timeoutReset"]] if d.get("timeoutReset") else []))
        for r in d.get("rows", []):
            res = r.get("result", {})
            if "I18Outcome" in res:                 # row I18: outcome and times only
                print(" | ".join([r.get("row", ""), "outcome=" + res["I18Outcome"], "state=" + res.get("State", "?"),
                                  "error=" + res.get("Error", "?"), "elapsed ms=" + res.get("ElapsedMs", "?"),
                                  "waited ms=" + res.get("WaitedMs", "?"), "cancel status=" + res.get("CancelStatus", "-"),
                                  "sheet=" + str(r.get("sheetSeen")),
                                  "verdict=" + r.get("verdict", "?"), r.get("note") or ""]))
                continue
            print(" | ".join([r.get("row", ""), r.get("scenario", ""), "sheet=" + str(r.get("sheetSeen")),
                              "state=" + res.get("State", "?"), "bucket=" + res.get("Bucket", "?"),
                              "decision=" + res.get("Decision", "?"), "logged=" + res.get("Logged", "?"),
                              "verdict=" + r.get("verdict", "?"), "check=" + str(r.get("check")) + " (expected " + str(r.get("expected")) + ")",
                              "matched=" + str(r.get("matchedProcess")) + "/" + str(r.get("matchedContainer"))]))
    d = load("restore-report")
    if isinstance(d, dict):
        print("restore-report: restored =", d.get("restored"), "| confirmation =", d.get("confirmation"))
    d = load("settings-alert")
    if isinstance(d, dict):
        print("settings-alert:", d.get("process"), "| title =", d.get("title"), "| knownButtons =", d.get("knownButtons"),
              "| otherButtons =", d.get("otherButtons"), "| knownTexts =", d.get("knownTexts"), "| otherTexts =", d.get("otherTexts"))
        print("settings-alert shape: secureTextFields =", d.get("secureTextFields"), "| textFields =", d.get("textFields"),
              "| identifier =", d.get("identifier"))
    d = load("settings-structure")
    if isinstance(d, list):
        for step in d:
            print("settings-structure:", step.get("screen"), "| matched =", step.get("matchedType"), "| navBar =", step.get("navBar"),
                  *(["| labels =", step["labels"]] if step.get("labels") else []))
d = load("original-scenario")                   # facts for the script, next to the attachments folder
restored = None
for key in ("exploration-report", "i18-report", "reinstall-probe"):
    r = load(key)
    if isinstance(r, dict):
        restored = r.get("restored")
with open(root + ".facts", "w") as fh:
    fh.write("original=%s\nrestored=%s\n" % ((d or {}).get("scenario") or "", restored))
if os.path.exists(manifest):                    # the manifest is only needed above
    os.remove(manifest)
sys.exit(5 if bad else 0)
'

# export_attachments <name> <mode>: exports, filters to the allowlist, scans, prints (scrubbed) output.
export_attachments() {
  local name="$1" mode="$2" drop="${3:-}" dir="$out/$1.attachments" output rc=0
  rm -rf "$dir"
  if ! xcrun xcresulttool export attachments --path "$out/$name.xcresult" --output-path "$dir" >/dev/null 2>&1; then
    rm -rf "$out/$name.xcresult" "$dir" "$dir.facts"
    echo "could not export attachments for $name" >&2
    return 1
  fi
  output="$(python3 -I -c "$PY" "$mode" "$dir" "$udid" "$device_name")" || rc=$?
  printf '%s\n' "$output" | scrub
  rm -rf "$out/$name.xcresult"
  if [[ "$drop" == drop ]]; then rm -rf "$dir" "$dir.facts"; fi
  return $rc
}

# Restores $original from a separate run and verifies it. The pending file is removed only after a verified restore.
restore_scenario() {
  local rc=0
  echo "restoring the original scenario from outside the runner"
  XENV=("TEST_RUNNER_RESTORE_SCENARIO=$original")
  run_xcb "RestoreTests/testRestore" restore || rc=$?
  XENV=()
  export_attachments restore summary drop || true   # also removes the result bundle
  if [[ $rc -eq 0 ]]; then
    rm -f "$pending"   # RestoreTests passes only when the scenario read back as selected
    echo "Age Assurance set back to $original (confirmation: read-back)"
    return 0
  fi
  echo "SCENARIO NOT RESTORED: $original"
  return 1
}

# After the main run (or an interrupt): export and print the attachments, read the original scenario and the restored flag from
# them, and decide whether the script must restore from outside.
finalize_main() {
  if [[ $main_done -eq 1 ]]; then return 0; fi
  main_done=1
  local rc=0 facts="$out/main.attachments.facts" restored=""
  export_attachments main summary || rc=$?
  if [[ -f "$facts" ]]; then
    original="$(sed -n 's/^original=//p' "$facts")"
    restored="$(sed -n 's/^restored=//p' "$facts")"
    rm -f "$facts"
  fi
  if [[ -f "$pending" && "$(cat "$pending")" == nothing ]]; then
    rm -f "$pending"
    echo "the test stopped before it changed anything; nothing to restore"
    return $rc
  fi
  if [[ "$original" != "Off" && "$original" != User* && -f "$pending" ]]; then original="$(cat "$pending")"; fi
  if [[ "$original" != "Off" && "$original" != User* ]]; then
    original=""
    if ! grep -q "Test Case .* started" "$out/main.scrubbed.log" 2>/dev/null; then
      rm -f "$pending"   # the test never started, so nothing was changed
      echo "the test did not start; nothing was changed"
    else
      echo "the original scenario was not recorded; if the run changed it, check the Age Assurance scenario by hand, then delete $pending"
    fi
  elif [[ "$restored" == read-back ]]; then
    rm -f "$pending"   # the test restored it and read the selection back (the exit status is kept as it is)
    echo "Age Assurance set back to $original (confirmation: $restored)"
  else
    echo "restore not confirmed by read-back (reported: ${restored:-none}); restoring from outside and keeping the pending file until that is verified"
    printf '%s\n' "$original" > "$pending"
    need_restore=1
  fi
  return $rc
}

# Every exit path (success, failure, INT, TERM) restores and then releases the lock; nothing exits in between.
on_exit() {
  local code=$?
  set +e
  trap '' INT TERM HUP
  trap - EXIT
  if [[ $main_started -eq 1 && $main_done -eq 0 ]]; then main_status=130; finalize_main || true; fi
  if [[ $need_restore -eq 1 ]]; then restore_scenario || true; fi
  release
  exit $code
}
trap on_exit EXIT
trap 'exit 130' INT TERM
trap 'exit 129' HUP

# Early check, then the per-device lock: mkdir is atomic and success means we hold it. Coordinate with other users of the device.
if other_test_running; then echo "an xcodebuild test for this device is already running; refusing" >&2; exit 3; fi
mkdir -p "$LOCK_DIR"
waited=0
while true; do
  if mkdir "$lock" 2>/dev/null; then held=1; break; fi
  owner_line="$(cat "$lock/owner" 2>/dev/null || echo unknown)"
  owner_pid="$(printf '%s' "$owner_line" | sed -n 's/.*pid=\([0-9][0-9]*\).*/\1/p')"
  if [[ -n "$owner_pid" ]] && ! kill -0 "$owner_pid" 2>/dev/null; then
    echo "note: lock owner process $owner_pid is not running; the lock is NOT broken, ask whoever started it to clear it" >&2
  fi
  if (( waited >= lock_timeout )); then
    echo "device lock is still held after ${lock_timeout}s; owner: $owner_line" >&2
    exit 4
  fi
  echo "device is in use (owner: $owner_line); waiting 15 s" >&2
  sleep 15; waited=$((waited + 15))
done
echo "$WHO $(date -u +%Y-%m-%dT%H:%M:%SZ) ui test $test_id pid=$$" > "$lock/owner"
# The lock is held now: check again that nobody started a test between the first check and the lock.
if other_test_running; then echo "an xcodebuild test for this device started meanwhile; refusing" >&2; exit 3; fi

(cd "$PROJECT_DIR" && xcodegen generate >/dev/null)

if [[ $reinstall -eq 1 ]]; then
  xcrun devicectl device uninstall app --device "$udid" "$SAMPLE_BUNDLE" 2>&1 | scrub
fi
if [[ -n "$app_path" ]]; then
  xcrun devicectl device install app --device "$udid" "$app_path" 2>&1 | scrub
fi

# A restore left pending by an earlier run comes first; if it cannot be verified, this run refuses to start.
if [[ -f "$pending" && "$(cat "$pending")" == unknown ]]; then
  echo "an earlier run may have changed the Age Assurance scenario and did not record the original: check the Age Assurance scenario by hand, then delete $pending" >&2
  exit 7
fi
if [[ -f "$pending" ]]; then
  original="$(cat "$pending")"
  echo "an earlier run left a scenario to restore; restoring it first"
  if ! restore_scenario; then echo "refusing to run until the pending restore succeeds (see $pending)" >&2; exit 7; fi
  original=""
fi

# The test reads the original scenario itself (and keeps one Settings session for all rows). The script decides afterwards,
# from the run's attachments, whether a restore from outside is needed.
XENV=()
if [[ $assume_off -eq 1 ]]; then XENV=("TEST_RUNNER_ASSUME_ORIGINAL=Off"); fi
echo "If the iPhone asks for Touch ID, touch the sensor (the test waits up to 120 s and never taps Cancel)."
main_started=1
printf 'unknown\n' > "$pending"   # replaced by the scenario label as soon as the test prints it
status=0
run_xcb "$test_id" main || status=$?
main_status=$status
echo "xcodebuild exit status: $status"
# The main run's allowed attachments (first-sheet evidence included) are kept even when the run failed.
finalize_main || status=$?
echo "results under bin/ios-uitests/$stamp (read only the .scrubbed.log files)"
exit $status
