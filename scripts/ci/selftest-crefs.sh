#!/usr/bin/env bash
# Negative control for scripts/check-public-types.cs --crefs: the check must reject a link to an internal overload and accept the public one.
#
#   scripts/ci/selftest-crefs.sh <path to the built Ageledger.Tests.dll>
#
# Uses the test-only type Ageledger.Tests.PackageDocsTests.OverloadFixture (public Run(int); internal Run(int, int) and Run()).
# Needs $RUNNER_TEMP and the .NET 10 SDK. Run it from the repository root. Exit code 0: the script behaved as expected; 1: it did not.
set -e
: "${RUNNER_TEMP:?RUNNER_TEMP must be set to a scratch directory}"
dll="${1:?usage: $0 <Ageledger.Tests.dll>}"
dir="$RUNNER_TEMP/cref-selftest"
rm -rf "$dir"
mkdir -p "$dir"
fail=0
t=Ageledger.Tests.PackageDocsTests.OverloadFixture
# expect <exit code> <cref id>
expect() {
  printf '<doc><members><member name="T:x"><see cref="%s"/></member></members></doc>\n' "$2" > "$dir/doc.xml"
  rc=0
  dotnet run scripts/check-public-types.cs -- --crefs "$dir/doc.xml" "$dll" > "$dir/out.txt" 2>&1 || rc=$?
  if [ "$rc" != "$1" ]; then
    echo "::error::Expected exit code $1 for cref $2 but got $rc."
    cat "$dir/out.txt"
    fail=1
  else
    echo "ok: exit $rc for $2"
  fi
}
expect 0 "M:$t.Run(System.Int32)"
expect 1 "M:$t.Run(System.Int32,System.Int32)"
expect 1 "M:$t.Run"
exit $fail
