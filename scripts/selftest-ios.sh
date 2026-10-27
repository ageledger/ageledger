#!/usr/bin/env bash
# Runs the sample app's on-device self-test (docs/device-test-plan.md, section 7.6) on one iPhone.
#
# Usage: scripts/selftest-ios.sh <device-udid>
#
# Needs a Debug build of the sample already installed, and the device unlocked. The UDID must be listed in the untracked allowlist
# (scripts/devices.local, see scripts/devices.local.example). Coordinate with other users of the device first: the script takes a lock
# for it (see AGELEDGER_LOCK_DIR in scripts/lib/selftest-common.sh), and refuses to start while an xcodebuild test for the same UDID is running.
# Only the sample is ended (devicectl's terminate-existing on launch); no device setting is changed. The self-test uses fakes only and
# never shows Apple's sheet. Exit code 0 only if every row passed.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib/selftest-common.sh
source "$here/lib/selftest-common.sh"

(($# == 1)) || st_die 2 "usage: $0 <device-udid>"
UDID=$1
st_check_id_shape "$UDID"
st_check_allowlist "$UDID" "${AGELEDGER_DEVICES_FILE:-$here/devices.local}"
st_refuse_if_xcodebuild_test "$UDID"
st_acquire_lock "$UDID" "ios self-test"
# Checked again now that the lock is held, in case a test started while this script waited for the lock.
st_refuse_if_xcodebuild_test "$UDID"

# FileSystem.AppDataDirectory is the app's Library folder on iOS. Confirmed on an iPhone on 2026-10-25; override if a later iOS version moves it.
RESULTS_SOURCE="${AGELEDGER_IOS_RESULTS_PATH:-Library/selftest-results.json}"

fetch_results() {
  xcrun devicectl device copy from --device "$UDID" --domain-type appDataContainer --domain-identifier "$PACKAGE" \
    --source "$RESULTS_SOURCE" --destination "$1" --timeout 30
}

# run_suite <suite>: launch (ending any running copy of the sample) with a fresh nonce, wait, check, print. Returns the report's exit code.
# devicectl cannot delete a file on the device, so a results file left by an earlier run is told apart by its nonce; the app also deletes it at the start of a run.
run_suite() {
  local suite=$1 nonce out
  nonce=$(st_new_nonce)
  out="$WORK_DIR/$suite.json"
  if ! xcrun devicectl device process launch --device "$UDID" --terminate-existing "$PACKAGE" -- \
    -ageledger-selftest "$suite" -ageledger-nonce "$nonce" >"$WORK_DIR/launch.log" 2>&1; then
    echo "launch failed for suite $suite (is the device unlocked and the Debug build installed?)" >&2
    st_show_launch_output "$WORK_DIR/launch.log" "$UDID"
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
  # L3: write entries, then the next launch ends the app (terminate-existing) and a new process reads them back.
  run_suite l3-write || status=1
  run_suite l3-read || status=1
fi

if ((status == 0)); then echo "SELFTEST OK"; else echo "SELFTEST FAILED" >&2; fi
exit $status
