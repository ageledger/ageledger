# shellcheck shell=bash
# Shared helpers for scripts/selftest-android.sh and scripts/selftest-ios.sh. Sourced, not run.
#
# Environment (all optional):
#   AGELEDGER_DEVICES_FILE      allowlist of device ids, one per line (default: scripts/devices.local, untracked)
#   AGELEDGER_LOCK_DIR          directory for per-device locks (default: /tmp/device-locks)
#   AGELEDGER_LOCK_WAIT         seconds to wait for a busy device before giving up (default: 600)
#   AGELEDGER_SELFTEST_TIMEOUT  seconds to wait for each suite's results (default: 360)
#
# Exit codes used by the scripts: 0 all rows passed; 1 a row failed, the run was incomplete or timed out;
# 2 refused or bad usage; 3 the device stayed busy. Internally, run_suite returns 4 when a suite could not be launched or timed out.

# shellcheck disable=SC2034  # used by the scripts that source this file
# shellcheck source=sample-id.sh
source "$(dirname "${BASH_SOURCE[0]}")/sample-id.sh"
PACKAGE="$SAMPLE_ID"
SELFTEST_TOOL="ageledger-selftest"
LOCK_ROOT="${AGELEDGER_LOCK_DIR:-/tmp/device-locks}"
LOCK_WAIT="${AGELEDGER_LOCK_WAIT:-600}"
SUITE_TIMEOUT="${AGELEDGER_SELFTEST_TIMEOUT:-360}"

LOCK_HELD=""
LOCK_OWNER_TEXT=""
WORK_DIR=""

st_die() {
  local code=$1
  shift
  echo "error: $*" >&2
  exit "$code"
}

# The device id ends up in a path and on command lines: allow only the characters real serials and UDIDs use.
st_check_id_shape() {
  [[ "$1" =~ ^[A-Za-z0-9._:-]{4,64}$ ]] || st_die 2 "the device id looks wrong; pass the serial or UDID as the first argument."
}

# Refuses any device that is not listed in the untracked allowlist file.
st_check_allowlist() {
  local id=$1 file=$2
  [[ -f "$file" ]] || st_die 2 "allowlist file not found ($file). Copy scripts/devices.local.example to scripts/devices.local and list your own devices."
  if ! sed -e 's/#.*//' -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' "$file" | grep -Fxq -- "$id"; then
    st_die 2 "this device is not in the allowlist file; refusing to touch it."
  fi
}

# Refuses to start while an xcodebuild test for this device is running.
st_refuse_if_xcodebuild_test() {
  local id=$1 procs
  procs=$(ps -axww -o command= 2>/dev/null || true)
  if grep -F "xcodebuild" <<<"$procs" | grep -F -- "$id" | grep -Eq '(^|[[:space:]])(test|test-without-building|build-for-testing)([[:space:]]|$)'; then
    st_die 2 "an xcodebuild test is running for this device; try again when it has finished."
  fi
}

st_cleanup() {
  if [[ -n "$WORK_DIR" && -d "$WORK_DIR" ]]; then
    rm -rf "$WORK_DIR"
  fi
  # Release only a lock this run holds: the owner file only if it still says what this run wrote, then the (empty) directory.
  if [[ -n "$LOCK_HELD" ]]; then
    if [[ ! -e "$LOCK_HELD/owner" ]]; then
      rmdir "$LOCK_HELD" 2>/dev/null || true   # created by this run, interrupted before the owner file was written
    elif [[ "$(cat "$LOCK_HELD/owner" 2>/dev/null)" == "$LOCK_OWNER_TEXT" ]]; then
      rm -f "$LOCK_HELD/owner" && rmdir "$LOCK_HELD" 2>/dev/null || true
    fi
    LOCK_HELD=""
  fi
}

# Takes the per-device lock (an atomic mkdir) and keeps it until the script ends. Waits while another user holds it.
st_acquire_lock() {
  local id=$1 what=$2 waited=0
  # Traps first, so an interrupt at any point after the mkdir still releases the lock (LOCK_HELD is empty until this run owns it).
  trap st_cleanup EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM
  trap 'exit 129' HUP
  trap 'exit 141' PIPE
  mkdir -p "$LOCK_ROOT"
  [[ -w "$LOCK_ROOT" ]] || st_die 2 "the lock directory is not writable ($LOCK_ROOT); set AGELEDGER_LOCK_DIR to a directory you can write to."
  while ! mkdir "$LOCK_ROOT/$id" 2>/dev/null; do
    echo "device is in use: $(cat "$LOCK_ROOT/$id/owner" 2>/dev/null || echo 'no owner file yet')" >&2
    if ((waited >= LOCK_WAIT)); then
      st_die 3 "the device stayed busy for ${LOCK_WAIT}s; not touching it. If the owner line above is stale (the holder is gone), the person who owns it removes it by hand."
    fi
    sleep 15
    waited=$((waited + 15))
  done
  LOCK_HELD="$LOCK_ROOT/$id"
  LOCK_OWNER_TEXT="$SELFTEST_TOOL $(date -u +%Y-%m-%dT%H:%M:%SZ) $what"
  printf '%s\n' "$LOCK_OWNER_TEXT" >"$LOCK_HELD/owner"
  WORK_DIR=$(mktemp -d "${TMPDIR:-/tmp}/ageledger-selftest.XXXXXX")
}

# st_show_launch_output <file> <device-id>: on a failed launch, prints the problem lines among the last lines of the launch output, with the
# device id replaced and quoted strings (which can hold a device name) redacted, before the work directory is deleted.
st_show_launch_output() {
  [[ -s "$1" ]] || return 0
  echo "last lines of the launch output:" >&2
  tail -n 10 "$1" | sed "s|$2|<device>|g" \
    | grep -iE 'error|failed|locked|not installed|denied|unavailable' \
    | sed -E "s/'[^']*'/'<redacted>'/g; s/\"[^\"]*\"/\"<redacted>\"/g" >&2 || true
}

# A fresh random nonce (32 lowercase hex characters).
st_new_nonce() {
  LC_ALL=C od -An -N16 -tx1 /dev/urandom | tr -d ' \n'
}

# Exit 0 when the file is JSON that carries this run's nonce.
st_results_ready() {
  python3 -I - "$1" "$2" <<'PY' 2>/dev/null
import json, sys
try:
    with open(sys.argv[1], encoding="utf-8") as f:
        data = json.load(f)
except Exception:
    sys.exit(1)
sys.exit(0 if isinstance(data, dict) and data.get("nonce") == sys.argv[2] else 1)
PY
}

# Prints the table for one results file. Exit 0 when complete and every case passed; 1 otherwise; 2 on a nonce or suite mismatch.
st_report() {
  python3 -I - "$1" "$2" "$3" <<'PY'
import json, sys
path, nonce, suite = sys.argv[1:4]
try:
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
except Exception:
    print("results file is not readable JSON")
    sys.exit(1)
if data.get("nonce") != nonce:
    print("nonce mismatch: this is not the results of this run")
    sys.exit(2)
if data.get("suite") != suite:
    print("suite mismatch")
    sys.exit(2)
if len(data.get("cases", [])) != data.get("expectedCases"):
    print("case count mismatch: %d cases, %s expected" % (len(data.get("cases", [])), data.get("expectedCases")))
    ok_count = False
else:
    ok_count = True
rows = [("row", "state", "bucket", "decision", "logged", "pass")]
for c in data.get("cases", []):
    name = c["row"] + ("/" + c["case"] if c.get("case") else "")
    def s(v):
        return "-" if v is None else str(v).lower() if isinstance(v, bool) else str(v)
    rows.append((name, s(c.get("state")), s(c.get("bucket")), s(c.get("decision")), s(c.get("logged")), ("FAIL" if not c.get("pass") else ("PASS" if c.get("asserted", True) else "RECORD"))))
widths = [max(len(r[i]) for r in rows) for i in range(6)]
for r in rows:
    print("  ".join(v.ljust(widths[i]) for i, v in enumerate(r)))
print("suite=%s platform=%s os=%s passed=%s failed=%s complete=%s" % (
    data.get("suite"), data.get("platform"), data.get("osVersion"), data.get("passed"), data.get("failed"), data.get("complete")))
ok = ok_count and bool(data.get("complete")) and data.get("failed") == 0 and len(rows) > 1
sys.exit(0 if ok else 1)
PY
}

# st_wait_for_results <fetch-function> <nonce> <out-file>: calls the fetch function (which writes the device's results file to $out)
# every 3 seconds until it holds this run's nonce or the suite timeout passes. Returns 1 on timeout.
st_wait_for_results() {
  local fetch=$1 nonce=$2 out=$3 deadline
  deadline=$(($(date +%s) + SUITE_TIMEOUT))
  while true; do
    rm -f "$out"
    "$fetch" "$out" >/dev/null 2>&1 || true
    if [[ -s "$out" ]] && st_results_ready "$out" "$nonce"; then
      return 0
    fi
    if (($(date +%s) >= deadline)); then
      return 1
    fi
    sleep 3
  done
}
