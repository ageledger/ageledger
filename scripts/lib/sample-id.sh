# shellcheck shell=bash
# The sample app's bundle id / Android package: ONE value for the scripts. Sourced, not run.
# Order: the environment variable AGELEDGER_SAMPLE_ID, then the FIRST line of scripts/sample-id.local (untracked; trimmed,
# CRLF-safe), then the project's default. samples/AgeledgerSample/AgeledgerSample.csproj reads the same two places
# (docs/try-the-sample.md). Letters and digits in dot-separated parts, each part starting with a letter: valid on iOS and Android.
# Sets SAMPLE_ID and exports AGELEDGER_SAMPLE_ID (XcodeGen reads it for the UI-test project) and
# TEST_RUNNER_AGELEDGER_SAMPLE_ID (xcodebuild passes it to the UI-test runner, where SampleApp.swift reads it).

_sid_file="$(CDPATH= cd -- "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/sample-id.local"
SAMPLE_ID="${AGELEDGER_SAMPLE_ID:-}"
if [[ -z "$SAMPLE_ID" && -e "$_sid_file" ]]; then
  if [[ ! -r "$_sid_file" || -d "$_sid_file" ]]; then
    echo "error: scripts/sample-id.local exists but cannot be read." >&2
    exit 2
  fi
  SAMPLE_ID="$(head -n 1 "$_sid_file" | tr -d '\r' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
fi
SAMPLE_ID="${SAMPLE_ID:-com.ageledger.sample}"
if [[ ! "$SAMPLE_ID" =~ ^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$ ]]; then
  echo "error: the sample id looks wrong (reverse-DNS letters and digits only, no hyphens, such as com.example.agesample)." >&2
  exit 2
fi
export AGELEDGER_SAMPLE_ID="$SAMPLE_ID"
export TEST_RUNNER_AGELEDGER_SAMPLE_ID="$SAMPLE_ID"
unset _sid_file
