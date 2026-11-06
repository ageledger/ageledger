#!/usr/bin/env bash
# Compiles ResultParser together with a small check and runs it. No device, no Xcode project needed.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
swiftc -o "$tmp/check" "$here/AgeledgerUITests/ResultParser.swift" "$here/Checks/main.swift"
"$tmp/check"

# Keep only assignments between S_1315 and ALL_SCENARIOS in the script: this range is executed here.
# The script's scenario labels must equal SandboxSettings.scenarios (same labels, same order).
script="$here/../../scripts/ios-uitests.sh"
from_script="$(bash -c "$(sed -n '/^S_1315=/,/^ALL_SCENARIOS=/p' "$script"); printf '%s\n' \"\${ALL_SCENARIOS[@]}\"")"
from_swift="$(sed -n '/static let scenarios = \[/,/^    \]/p' "$here/AgeledgerUITests/SandboxSettings.swift" | grep -o '"[^"]*"' | sed 's/^"//; s/"$//')"
if [[ -z "$from_script" || "$from_script" != "$from_swift" ]]; then
  echo "FAIL: the scenario list in scripts/ios-uitests.sh differs from SandboxSettings.scenarios" >&2
  exit 1
fi
echo "scenario lists equal"

# The usage line prints without a device (it exits before the allowlist, the lock and any xcodebuild).
if ! "$here/../../scripts/ios-uitests.sh" --row i7b --scenario "User 13-15, significant change approved" 2>&1 | grep -q '^usage:'; then
  echo "FAIL: scripts/ios-uitests.sh did not print its usage line" >&2
  exit 1
fi
echo "usage line ok"
