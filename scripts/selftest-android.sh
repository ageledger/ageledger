#!/usr/bin/env bash
# Runs the sample app's on-device self-test (docs/device-test-plan.md, section 7.6) on one Android device or emulator.
#
# Usage: scripts/selftest-android.sh <adb-serial> [--real-play]
#
# --real-play also runs the opt-in suite that calls the real Play service (row A20 or A21). It may show Play's own consent screen, and
# its answer can describe a real account: only the state and error category are recorded. Without the flag the run uses fakes only.
#
# Needs a Debug build of the sample already installed, and the device unlocked. The serial must be listed in the untracked allowlist
# (scripts/devices.local, see scripts/devices.local.example). Coordinate with other users of the device first: the script takes a lock
# for it (see AGELEDGER_LOCK_DIR in scripts/lib/selftest-common.sh) but cannot know about people using it by hand.
# Only the sample is stopped; no device setting is changed. Exit code 0 only if every row passed.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib/selftest-common.sh
source "$here/lib/selftest-common.sh"

(($# >= 1 && $# <= 2)) || st_die 2 "usage: $0 <adb-serial> [--real-play]"
SERIAL=$1
REAL_PLAY=0
if (($# == 2)); then
  [[ "$2" == "--real-play" ]] || st_die 2 "usage: $0 <adb-serial> [--real-play]"
  REAL_PLAY=1
fi
ADB="${ADB:-adb}"
st_check_id_shape "$SERIAL"
st_check_allowlist "$SERIAL" "${AGELEDGER_DEVICES_FILE:-$here/devices.local}"
st_acquire_lock "$SERIAL" "android self-test"

adb_s() { "$ADB" -s "$SERIAL" "$@"; }

[[ "$(adb_s get-state 2>/dev/null)" == "device" ]] || st_die 1 "the device is not connected and ready."
[[ -n "$(adb_s shell pm path "$PACKAGE" 2>/dev/null | tr -d '\r')" ]] || st_die 1 "the sample is not installed on the device (install a Debug build first)."

# run-as works only for a debuggable (Debug) build: fail fast on a Release build.
adb_s shell run-as "$PACKAGE" true >/dev/null 2>&1 || st_die 1 "not a Debug build (run-as is refused); install a Debug build of the sample."

RESULTS_PATH="files/selftest-results.json"

fetch_results() {
  # run-as works only for a debuggable (Debug) build. A missing file leaves an error text, which fails the nonce check.
  adb_s shell run-as "$PACKAGE" cat "$RESULTS_PATH" | tr -d '\r' >"$1"
}

# run_suite <suite>: stop the sample, delete old results, launch with a fresh nonce, wait, check, print. Returns the report's exit code.
run_suite() {
  local suite=$1 nonce out started
  nonce=$(st_new_nonce)
  out="$WORK_DIR/$suite.json"
  adb_s shell am force-stop "$PACKAGE" >/dev/null 2>&1
  adb_s shell run-as "$PACKAGE" rm -f "$RESULTS_PATH" "$RESULTS_PATH.tmp" >/dev/null 2>&1
  started=$(adb_s shell am start -W -n "$PACKAGE/$PACKAGE.MainActivity" --es ageledger_selftest "$suite" --es ageledger_nonce "$nonce" 2>&1 | tr -d '\r')
  if grep -qiE 'error|exception' <<<"$started"; then
    echo "launch failed for suite $suite" >&2
    printf '%s\n' "$started" >"$WORK_DIR/launch.log"
    st_show_launch_output "$WORK_DIR/launch.log" "$SERIAL"
    return 4
  fi
  echo "== suite $suite =="
  if ! st_wait_for_results fetch_results "$nonce" "$out"; then
    echo "timed out after ${SUITE_TIMEOUT}s waiting for the results of suite $suite" >&2
    return 4
  fi
  st_report "$out" "$nonce" "$suite"
}

status=0
run_suite all
rc=$?
if ((rc != 0)); then status=1; fi
if ((rc == 4)); then
  echo "skipping the restart check (l3): the main suite did not run" >&2
else
  # L3: write entries, end the app, then read them back in a new process.
  run_suite l3-write || status=1
  run_suite l3-read || status=1
fi
if ((REAL_PLAY == 1)); then run_suite real-play || status=1; fi

if ((status == 0)); then echo "SELFTEST OK"; else echo "SELFTEST FAILED" >&2; fi
exit $status
