#!/usr/bin/env bash
# Tests scripts/ci/push-packages.sh with a fake dotnet on PATH: every row of its small decision table, and the way a 409 is recognised.
# Needs $RUNNER_TEMP. Run it from the repository root. Exit code 0: all cases behaved as expected; 1: one did not.
set -e
: "${RUNNER_TEMP:?RUNNER_TEMP must be set to a scratch directory}"
root=$(pwd)
work="$RUNNER_TEMP/push-selftest"
rm -rf "$work"
mkdir -p "$work/bin"
cat > "$work/bin/dotnet" <<'FAKE'
#!/usr/bin/env bash
# fake "dotnet nuget push <file> ...": the result is chosen by the package, from STUB_MAIN and STUB_TESTING
f=$(basename "$3")
echo "$f" >> "$FAKE_LOG"
echo "$*" >> "$FAKE_ARGS"
case "$f" in
  Ageledger.Testing.*) r=${STUB_TESTING:-ok} ;;
  *) r=${STUB_MAIN:-ok} ;;
esac
case "$r" in
  ok) echo "Your package was pushed." ;;
  conflict) echo "  Conflict https://www.nuget.org/api/v2/package 201ms"; echo "error: Response status code does not indicate success: 409 (Conflict - The package already exists)."; exit 1 ;;
  forbidden) echo "  BadRequest https://www.nuget.org/api/v2/package 409ms"; echo "error: Response status code does not indicate success: 403 (Forbidden - the key may not push; a package that already exists is not the cause)."; exit 1 ;;
  server) echo "error: Response status code does not indicate success: 500 (Internal Server Error) while pushing $f."; exit 1 ;;
  *) echo "fake dotnet: unknown STUB value '$r'" >&2; exit 99 ;;
esac
FAKE
chmod +x "$work/bin/dotnet"
fail=0
# case_ <name> <expected exit> <expected pushes, space separated, in order> <expected output pattern, '-' for none, or '!pattern' that must not appear> <VAR=value ...>
case_() {
  local name="$1" want_rc="$2" want_pushes="$3" want_out="$4" rc=0 got n
  shift 4
  : > "$work/log"
  : > "$work/args"
  env PATH="$work/bin:$PATH" FAKE_LOG="$work/log" FAKE_ARGS="$work/args" PKG_DIR=/pkgs NUGET_API_KEY=k VERSION=1.0.0 MAIN_EXISTS=no TESTING_EXISTS=no "$@" \
    bash "$root/scripts/ci/push-packages.sh" > "$work/out" 2>&1 || rc=$?
  got=$(tr '\n' ' ' < "$work/log" | sed 's/ $//')
  n=$(wc -l < "$work/args" | tr -d ' ')
  # a symbol package is never pushed on its own (the .nupkg takes its .snupkg with it)
  if grep -q 'snupkg' "$work/log"; then
    echo "::error::Case '$name': a .snupkg was pushed on its own:"
    cat "$work/log"
    fail=1
  fi
  # every push must name the source and the key and never skip duplicates
  if grep -q -- '--skip-duplicate' "$work/args" \
     || [ "$(grep -c -- '--source https://api.nuget.org/v3/index.json' "$work/args")" != "$n" ] \
     || [ "$(grep -c -- '--api-key k' "$work/args")" != "$n" ]; then
    echo "::error::Case '$name': a push had --skip-duplicate or lacked --source/--api-key:"
    cat "$work/args"
    fail=1
  fi
  if [ "$rc" != "$want_rc" ] || [ "$got" != "$want_pushes" ] \
     || { [ "$want_out" != "-" ] && [ "${want_out#!}" = "$want_out" ] && ! grep -Eq "$want_out" "$work/out"; } \
     || { [ "${want_out#!}" != "$want_out" ] && grep -Eq "${want_out#!}" "$work/out"; }; then
    echo "::error::Case '$name': exit $rc (wanted $want_rc), pushes '$got' (wanted '$want_pushes'), output pattern '$want_out'."
    cat "$work/out"
    fail=1
  else
    echo "ok: $name"
  fi
}
M=Ageledger.1.0.0.nupkg T=Ageledger.Testing.1.0.0.nupkg
case_ "all succeed" 0 "$M $T" "-"
case_ "Ageledger exists: refused, nothing pushed" 1 "" "finished by hand" MAIN_EXISTS=yes
case_ "Ageledger.Testing exists: refused, nothing pushed" 1 "" "finished by hand" TESTING_EXISTS=yes
case_ "both exist: refused, nothing pushed" 1 "" "finished by hand" MAIN_EXISTS=yes TESTING_EXISTS=yes
case_ "MAIN_EXISTS=true is rejected" 1 "" "must each be" MAIN_EXISTS=true
case_ "MAIN_EXISTS=Yes is rejected" 1 "" "must each be" MAIN_EXISTS=Yes
case_ "TESTING_EXISTS empty is rejected" 1 "" "must each be|not set" TESTING_EXISTS=
case_ "Ageledger fails (not 409): unknown state, check nuget.org, Testing is not pushed" 1 "$M" "Check nuget\.org first" STUB_MAIN=server
case_ "Ageledger fails (not 409): does not claim anything is published" 1 "$M" "!is published" STUB_MAIN=server
case_ "Ageledger fails (not 409): says it may or may not be there" 1 "$M" "may or may not be on nuget.org" STUB_MAIN=server
case_ "Ageledger answers 409: Testing is not pushed, check nuget.org" 1 "$M" "Check nuget.org first" STUB_MAIN=conflict
case_ "Ageledger.Testing fails after Ageledger: the missing file is named" 1 "$M $T" "Ageledger\.Testing\.1\.0\.0\.nupkg \(and its \.snupkg\) is missing\. Do not re-run; push the missing file by hand" STUB_TESTING=server
case_ "Ageledger.Testing answers 409: already exists or validating, not 'missing'" 1 "$M $T" "already exists on nuget.org or is still being validated" STUB_TESTING=conflict
case_ "Ageledger.Testing answers 409: does not say it is missing" 1 "$M $T" "!is missing" STUB_TESTING=conflict
case_ "NUGET_API_KEY empty is rejected" 1 "" "NUGET_API_KEY" NUGET_API_KEY=
case_ "PKG_DIR empty is rejected" 1 "" "PKG_DIR" PKG_DIR=
case_ "a 403 with a 409ms timing line is not a conflict" 1 "$M" "!already exists on nuget.org" STUB_MAIN=forbidden
case_ "a 500 for version 1.0.409 is not a conflict" 1 "Ageledger.1.0.409.nupkg" "!already exists on nuget.org" STUB_MAIN=server VERSION=1.0.409
case_ "an unknown STUB value fails (exit 99 from the fake)" 1 "$M" "unknown STUB value 'bogus'" STUB_MAIN=bogus
exit $fail
