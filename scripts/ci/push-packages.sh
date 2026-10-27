#!/usr/bin/env bash
# Pushes the release packages to nuget.org: Ageledger first (its .snupkg goes with it), then Ageledger.Testing, which depends on it.
# Called by release.yml; scripts/ci/selftest-release-push.sh tests it with a fake dotnet.
#
# There is no automatic recovery. Any failure ends the script with exit 1, and a half-finished release is finished by hand (see
# CONTRIBUTING, "If a release stops half way"). A workflow re-run is never the way to finish one.
#
# Environment: VERSION, PKG_DIR (folder with the four package files), NUGET_API_KEY, and what nuget.org already has, as found by the
# workflow: MAIN_EXISTS and TESTING_EXISTS, which must both be "no" (nothing is pushed otherwise). NUGET_SOURCE is optional.
# There is no --skip-duplicate.
set -e
: "${VERSION:?}" "${PKG_DIR:?}" "${NUGET_API_KEY:?}" "${MAIN_EXISTS:?}" "${TESTING_EXISTS:?}"
for v in "$MAIN_EXISTS" "$TESTING_EXISTS"; do
  case "$v" in
    yes|no) ;;
    *) echo "::error::MAIN_EXISTS and TESTING_EXISTS must each be 'yes' or 'no', not '$v'; nothing was pushed."; exit 1 ;;
  esac
done
if [ "$MAIN_EXISTS" != "no" ] || [ "$TESTING_EXISTS" != "no" ]; then
  echo "::error::Ageledger $VERSION or Ageledger.Testing $VERSION is already on nuget.org; nothing was pushed. A half-finished release is finished by hand (CONTRIBUTING, 'If a release stops half way')."
  exit 1
fi
source_url="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

# push <file>: prints dotnet's output; returns 0 pushed, 3 nuget.org answered 409 (already there, or still validating), 1 any other failure.
# The script never continues after a failure. A 409 is recognised only by the status line .NET writes for a conflict, "Response status
# code does not indicate success: 409 (...)" (the HttpRequestException message NuGet prints; not observed from nuget.org). A bare "409"
# elsewhere in the output (a timing such as "409ms", a version such as 1.0.409) does not count.
push() {
  local file="$1" rc=0 out
  out=$(dotnet nuget push "$file" --api-key "$NUGET_API_KEY" --source "$source_url" --timeout 600 2>&1) || rc=$?
  printf '%s\n' "$out"
  if [ "$rc" = "0" ]; then return 0; fi
  if grep -Eq 'Response status code does not indicate success: 409 \(' <<<"$out"; then
    echo "::error::$(basename "$file") already exists on nuget.org or is still being validated."
    return 3
  fi
  echo "::error::Pushing $(basename "$file") failed."
  return 1
}

main_nupkg="$PKG_DIR/Ageledger.$VERSION.nupkg"
testing_nupkg="$PKG_DIR/Ageledger.Testing.$VERSION.nupkg"

rc=0
push "$main_nupkg" || rc=$?
if [ "$rc" != "0" ]; then
  # Nothing is known to be missing here: the push may have gone through on nuget.org's side (and the .snupkg goes with it).
  echo "::error::Ageledger $VERSION may or may not be on nuget.org (it can still be validating; its .snupkg goes with it). Do not use Re-run all jobs. Check nuget.org first, then follow CONTRIBUTING 'If a release stops half way'."
  exit 1
fi
rc=0
push "$testing_nupkg" || rc=$?
if [ "$rc" = "3" ]; then
  echo "::error::Ageledger $VERSION was pushed, but Ageledger.Testing $VERSION already exists on nuget.org or is still being validated. Do not re-run; check nuget.org and follow CONTRIBUTING 'If a release stops half way'."
  exit 1
elif [ "$rc" != "0" ]; then
  echo "::error::Ageledger $VERSION is published but $(basename "$testing_nupkg") (and its .snupkg) is missing. Do not re-run; push the missing file by hand (CONTRIBUTING, 'If a release stops half way')."
  exit 1
fi
