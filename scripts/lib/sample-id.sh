# shellcheck shell=bash
# The sample app's bundle id / Android package: ONE value for the scripts. Sourced, not run.
# Order: the environment variable AGELEDGER_SAMPLE_ID, then the id alone in scripts/sample-id.local (untracked), then the
# project's default. samples/AgeledgerSample/AgeledgerSample.csproj reads the same two places (docs/try-the-sample.md).
# Sets SAMPLE_ID and exports AGELEDGER_SAMPLE_ID (XcodeGen reads it for the UI-test project) and
# TEST_RUNNER_AGELEDGER_SAMPLE_ID (xcodebuild passes it to the UI-test runner, where SampleApp.swift reads it).

_sid_file="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/sample-id.local"
SAMPLE_ID="${AGELEDGER_SAMPLE_ID:-}"
if [[ -z "$SAMPLE_ID" && -f "$_sid_file" ]]; then
  SAMPLE_ID="$(tr -d '[:space:]' <"$_sid_file")"
fi
SAMPLE_ID="${SAMPLE_ID:-com.ageledger.sample}"
if [[ ! "$SAMPLE_ID" =~ ^[A-Za-z][A-Za-z0-9-]*(\.[A-Za-z][A-Za-z0-9-]*)+$ ]]; then
  echo "error: the sample id looks wrong (use a reverse-DNS id such as com.example.agesample)." >&2
  exit 2
fi
export AGELEDGER_SAMPLE_ID="$SAMPLE_ID"
export TEST_RUNNER_AGELEDGER_SAMPLE_ID="$SAMPLE_ID"
unset _sid_file
