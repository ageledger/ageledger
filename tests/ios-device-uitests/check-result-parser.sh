#!/usr/bin/env bash
# Compiles ResultParser together with a small check and runs it. No device, no Xcode project needed.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
swiftc -o "$tmp/check" "$here/AgeledgerUITests/ResultParser.swift" "$here/Checks/main.swift"
"$tmp/check"
