#!/usr/bin/env bash
# Checks the packed NuGet packages. Used by the ci and release workflows so both run exactly the same checks.
#
#   scripts/ci/check-packages.sh main <dir holding Ageledger.<version>.nupkg and .snupkg>
#   scripts/ci/check-packages.sh testing <dir holding Ageledger.Testing.<version>.nupkg and .snupkg> <dir of the main package>
#
# Needs $RUNNER_TEMP (scratch space), python3, the .NET 10 SDK and the macOS tools dwarfdump, codesign and PlistBuddy.
# Run it from the repository root. Exit code 0: all checks passed; 1: a check failed (printed as ::error:: lines); 2: usage.
set -e
: "${RUNNER_TEMP:?RUNNER_TEMP must be set to a scratch directory}"


# The package must carry the iOS binding inside it (it is not a separate package) next to the iOS library, and its README and a NOTICE (with the copyright line) at its root, must not depend on it,
# and neither package nor the binding's native-framework archive may contain a machine path.
check_main() {
main_dir="$1"
pkg=$(ls "$main_dir"/Ageledger.[0-9]*.nupkg)
spkg=$(ls "$main_dir"/Ageledger.[0-9]*.snupkg)
listing=$(unzip -l "$pkg")
fail=0
check() { if ! grep -Eq "$1" <<<"$listing"; then echo "::error::The package is missing $2."; fail=1; fi; }
check 'lib/net10\.0-ios[0-9.]*/Ageledger\.IosBinding\.dll$' "the iOS binding assembly"
check 'lib/net10\.0-ios[0-9.]*/Ageledger\.IosBinding\.resources\.zip$' "the iOS binding's native framework (resources.zip)"
check '(^| )README\.md$' "README.md"
# Debug symbols belong in the .snupkg only.
if grep -Eq '\.pdb$' <<<"$listing"; then
  echo "::error::The package contains a .pdb file; symbols belong in the .snupkg."
  fail=1
fi
# The binding files must sit in the same lib folder as the iOS library, or an app would not get both.
iosdir=$(grep -Eo 'lib/net10\.0-ios[0-9.]*/' <<<"$listing" | sort -u | head -n 1)
if [ -z "$iosdir" ] || ! grep -Fq "${iosdir}Ageledger.dll" <<<"$listing"; then
  echo "::error::lib/<ios tfm>/Ageledger.dll is not in the same folder as the iOS binding files."
  fail=1
fi
check '(^| )NOTICE$' "NOTICE at the package root"
if ! unzip -p "$pkg" NOTICE 2>/dev/null | grep -Fq 'Copyright 2026 The Ageledger Authors'; then
  echo "::error::The package's root NOTICE is missing or does not contain the copyright line."
  fail=1
fi
nuspec=$(unzip -p "$pkg" '*.nuspec')
if ! grep -Fq '<authors>Ageledger</authors>' <<<"$nuspec"; then
  echo "::error::The package nuspec does not have <authors>Ageledger</authors>."
  fail=1
fi
if ! grep -Fq '<copyright>Copyright 2026 The Ageledger Authors</copyright>' <<<"$nuspec"; then
  echo "::error::The package nuspec does not have the copyright line 'Copyright 2026 The Ageledger Authors'."
  fail=1
fi
if grep -q 'Ageledger\.IosBinding' <<<"$nuspec"; then
  echo "::error::The nuspec depends on Ageledger.IosBinding; it must be bundled, not referenced."
  fail=1
fi
# No build-machine path anywhere: the packages, and the archive nested inside the nupkg. Text and binary files are searched separately.
rm -rf "$RUNNER_TEMP/inspect"
mkdir -p "$RUNNER_TEMP/inspect/nupkg" "$RUNNER_TEMP/inspect/snupkg" "$RUNNER_TEMP/inspect/resources"
unzip -q "$pkg" -d "$RUNNER_TEMP/inspect/nupkg"
# The fakes live in the separate Ageledger.Testing package: no assembly of that name and no fake type in any assembly of this one.
if grep -Eq 'Ageledger\.Testing' <<<"$listing"; then
  echo "::error::The package contains an Ageledger.Testing file."
  fail=1
fi
fakes=$(grep -rlaE 'Fake(Apple|Android)[A-Za-z]*Backend|Ageledger\.Testing' --include='*.dll' "$RUNNER_TEMP/inspect/nupkg" || true)
if [ -n "$fakes" ]; then
  echo "::error::A fake type or an Ageledger.Testing reference is in the main package's assemblies:"
  echo "$fakes"
  fail=1
fi
# No analyzer package may leak into the dependencies, and each target framework group lists exactly the expected package ids.
if grep -q 'CodeAnalysis' <<<"$nuspec"; then
  echo "::error::The nuspec lists a CodeAnalysis dependency; the analyzer must be private."
  fail=1
fi
python3 -I - "$RUNNER_TEMP/inspect/nupkg/Ageledger.nuspec" <<'PY' || fail=1
import sys, xml.etree.ElementTree as ET
expected = {
    "net10.0": set(),
    "net10.0-android": {"Xamarin.Google.Android.Play.Age.Signals", "Xamarin.AndroidX.Lifecycle.LiveData.Core"},
    "net10.0-ios": set(),
}
groups = {}
for el in ET.parse(sys.argv[1]).iter():
    if el.tag.endswith("}group") or el.tag == "group":
        tfm = el.get("targetFramework", "")
        key = next((k for k in sorted(expected, key=len, reverse=True) if tfm == k or tfm.startswith(k)), tfm)
        groups[key] = {d.get("id") for d in el if d.tag.endswith("dependency")}
ok = True
for key, ids in groups.items():
    if ids != expected.get(key):
        print(f"::error::Dependency group {key} lists {sorted(ids)}, expected {sorted(expected.get(key, ['(unexpected group)']))}.")
        ok = False
for key in expected:
    if key not in groups:
        print(f"::error::Dependency group {key} is missing from the nuspec.")
        ok = False
sys.exit(0 if ok else 1)
PY
# The iOS binding is an implementation detail: none of its types may be public (the allowlist is empty).
bdll=$(ls "$RUNNER_TEMP"/inspect/nupkg/lib/net10.0-ios*/Ageledger.IosBinding.dll)
dotnet run scripts/check-public-types.cs -- "$bdll" || fail=1
# Doc comments in every packed Ageledger.xml (net10.0, Android, iOS) may only link to public or protected members.
# Exactly three XML files are expected (net10.0, Android, iOS); a cref may point into the iOS binding too, so that one is given as well.
xmls=$(ls "$RUNNER_TEMP"/inspect/nupkg/lib/*/Ageledger.xml 2>/dev/null | wc -l | tr -d ' ')
if [ "$xmls" != "3" ]; then
  echo "::error::Expected 3 packed Ageledger.xml files, found $xmls."
  fail=1
fi
for xml in "$RUNNER_TEMP"/inspect/nupkg/lib/*/Ageledger.xml; do
  extra=()
  if [ -f "$(dirname "$xml")/Ageledger.IosBinding.dll" ]; then extra=("$(dirname "$xml")/Ageledger.IosBinding.dll"); fi
  dotnet run scripts/check-public-types.cs -- --crefs "$xml" "${xml%.xml}.dll" "${extra[@]}" || fail=1
done
unzip -q "$spkg" -d "$RUNNER_TEMP/inspect/snupkg"
for z in "$RUNNER_TEMP"/inspect/nupkg/lib/*/*.resources.zip; do
  unzip -q "$z" -d "$RUNNER_TEMP/inspect/resources/$(basename "$z")"
done
for mode in -rIl -rl; do
  leaks=$(grep -E $mode '/(Users|home)/' "$RUNNER_TEMP/inspect" || true)
  if [ -n "$leaks" ]; then
    echo "::error::A machine path (/Users/ or /home/) is in the package contents:"
    echo "$leaks"
    fail=1
  fi
done
# PDB document names are stored as separate parts, so "/Users/" alone can miss them: check the names themselves. Every document in the
# symbol packages must be under /_/ (deterministic source paths); a build-machine path would not be.
dotnet run scripts/check-public-types.cs -- --pdb-paths "$RUNNER_TEMP/inspect/snupkg" || fail=1
# The dSYMs in the native-framework archive. This must not pass by checking nothing: the number found has to equal the number the
# xcframework's Info.plist declares (and be at least 2, device + simulator). Each one needs a non-empty DWARF file, the same UUID as its
# slice's binary, a line table for the wrapper, no temporary or volume path anywhere in the bundle, and no absolute path other than the
# mapped SDK root (/SDK), the synthetic /<compiler-generated> and a bare "/" in any debug-info or debug-line string.
found=0
expected=0
for xc in "$RUNNER_TEMP"/inspect/resources/*/AgeledgerNative.xcframework; do
  [ -f "$xc/Info.plist" ] || continue
  n=$(/usr/libexec/PlistBuddy -c "Print :AvailableLibraries" "$xc/Info.plist" | grep -c DebugSymbolsPath || true)
  expected=$((expected + n))
done
for d in "$RUNNER_TEMP"/inspect/resources/*/AgeledgerNative.xcframework/*/dSYMs/*.dSYM; do
  [ -d "$d" ] || continue
  found=$((found + 1))
  dw=$(find "$d" -path '*/Resources/DWARF/*' -type f | head -n 1)
  if [ -z "$dw" ] || [ ! -s "$dw" ]; then
    echo "::error::A dSYM has a missing or empty DWARF file ($d)."
    fail=1
    continue
  fi
  bin="${d%/dSYMs/*}/AgeledgerNative.framework/AgeledgerNative"
  errf="$RUNNER_TEMP/dwarfdump.err"
  : >"$errf"
  if ! duuid=$(dwarfdump --uuid "$dw" 2>>"$errf") || ! buuid=$(dwarfdump --uuid "$bin" 2>>"$errf"); then
    echo "::error::dwarfdump --uuid failed for a dSYM or its binary ($d)."
    cat "$errf"
    fail=1
    continue
  fi
  duuid=$(awk '/^UUID:/{print $2; exit}' <<<"$duuid")
  buuid=$(awk '/^UUID:/{print $2; exit}' <<<"$buuid")
  if [ -z "$duuid" ] || [ "$duuid" != "$buuid" ]; then
    echo "::error::A dSYM's UUID does not match its binary's ($d)."
    fail=1
  fi
  : >"$errf"
  if ! info=$(dwarfdump --debug-info "$dw" 2>>"$errf") || ! lines=$(dwarfdump --debug-line "$dw" 2>>"$errf"); then
    echo "::error::dwarfdump --debug-info or --debug-line failed for a dSYM ($d)."
    cat "$errf"
    fail=1
    continue
  fi
  dump="$info"$'\n'"$lines"
  bad=$(grep -oE '"/[^"]*"' <<<"$dump" | grep -Ev '^"(/SDK(/[^"]*)?|/<compiler-generated>|/)"$' | sort -u || true)
  if [ -n "$bad" ]; then
    echo "::error::A dSYM names an absolute path other than the mapped SDK root ($d):"
    echo "$bad"
    fail=1
  fi
  if grep -raE '/private/|/var/folders/|/tmp/|/Volumes/' "$d" >/dev/null; then
    echo "::error::A dSYM bundle contains a temporary or volume path ($d)."
    fail=1
  fi
  if ! grep -Fq '"/SDK' <<<"$dump"; then
    echo "::error::A dSYM has no /SDK path strings, so the path check saw no paths ($d)."
    fail=1
  fi
  # Only the line table is searched, and it must hold at least one address row, so a file-name table without line entries does not pass.
  if ! grep -Fq 'AgeSignalsBridge.swift' <<<"$lines" || ! grep -Eq '^0x[0-9a-fA-F]+ ' <<<"$lines"; then
    echo "::error::A dSYM's line table has no address rows or does not name AgeSignalsBridge.swift ($d)."
    fail=1
  fi
done
# The simulator slice's signature must not be broken by the strip step. It is copied out of the .framework folder first: inside it,
# codesign also looks for sealed resources and reports "code has no resources", which a bare copy does not. Exactly one such slice is expected.
# The device slice is re-signed when an app embeds it, so it is not checked.
simcount=0
for b in "$RUNNER_TEMP"/inspect/resources/*/AgeledgerNative.xcframework/*-simulator/AgeledgerNative.framework/AgeledgerNative; do
  [ -f "$b" ] || continue
  simcount=$((simcount + 1))
  cp "$b" "$RUNNER_TEMP/sim-bin"
  if ! out=$(codesign --verify "$RUNNER_TEMP/sim-bin" 2>&1); then
    echo "::error::The simulator binary's signature does not verify: ${out//"$RUNNER_TEMP/sim-bin"/the binary}"
    fail=1
  fi
done
if [ "$simcount" -ne 1 ]; then
  echo "::error::Expected exactly 1 simulator binary to check, found $simcount."
  fail=1
fi
if [ "$found" -lt 2 ] || [ "$found" -ne "$expected" ]; then
  echo "::error::Found $found dSYMs in the native-framework archive but its Info.plist declares $expected (at least 2 are required)."
  fail=1
else
  echo "Checked $found dSYM(s) against $expected declared."
fi
exit $fail
}

# The test-double package: its own README, NOTICE with the copyright line, a dependency on Ageledger, the assembly, and no machine path.
check_testing() {
testing_dir="$1"
main_dir="$2"
tpkg=$(ls "$testing_dir"/Ageledger.Testing.[0-9]*.nupkg)
tspkg=$(ls "$testing_dir"/Ageledger.Testing.[0-9]*.snupkg)
listing=$(unzip -l "$tpkg")
fail=0
check() { if ! grep -Eq "$1" <<<"$listing"; then echo "::error::The Ageledger.Testing package is missing $2."; fail=1; fi; }
check 'lib/net10\.0/Ageledger\.Testing\.dll$' "its assembly"
if grep -Eq 'lib/[^/]+/Ageledger\.dll$' <<<"$listing"; then
  echo "::error::The Ageledger.Testing package contains Ageledger.dll; it must only depend on Ageledger."
  fail=1
fi
check '(^| )README\.md$' "README.md"
if grep -Eq '\.pdb$' <<<"$listing"; then
  echo "::error::The Ageledger.Testing package contains a .pdb file; symbols belong in the .snupkg."
  fail=1
fi
check '(^| )NOTICE$' "NOTICE at the package root"
if ! unzip -p "$tpkg" NOTICE 2>/dev/null | grep -Fq 'Copyright 2026 The Ageledger Authors'; then
  echo "::error::The Ageledger.Testing NOTICE is missing or does not contain the copyright line."
  fail=1
fi
nuspec=$(unzip -p "$tpkg" '*.nuspec')
if ! grep -Fq '<authors>Ageledger</authors>' <<<"$nuspec"; then
  echo "::error::The Ageledger.Testing nuspec does not have <authors>Ageledger</authors>."
  fail=1
fi
if ! grep -Fq '<copyright>Copyright 2026 The Ageledger Authors</copyright>' <<<"$nuspec"; then
  echo "::error::The Ageledger.Testing nuspec does not have the copyright line 'Copyright 2026 The Ageledger Authors'."
  fail=1
fi
# The dependency must be exactly the main package's version, written [version].
pkg=$(ls "$main_dir"/Ageledger.[0-9]*.nupkg)
mainver=$(unzip -p "$pkg" Ageledger.nuspec | sed -n 's|.*<version>\(.*\)</version>.*|\1|p' | head -n 1)
if [ -z "$mainver" ] || ! grep -Fq "<dependency id=\"Ageledger\" version=\"[$mainver]\"" <<<"$nuspec"; then
  echo "::error::The Ageledger.Testing nuspec must depend on Ageledger exactly [$mainver]."
  fail=1
fi
if grep -q 'CodeAnalysis' <<<"$nuspec"; then
  echo "::error::The Ageledger.Testing nuspec lists an analyzer dependency; it must be private."
  fail=1
fi
rm -rf "$RUNNER_TEMP/inspect-testing"
mkdir -p "$RUNNER_TEMP/inspect-testing/nupkg" "$RUNNER_TEMP/inspect-testing/snupkg"
unzip -q "$tpkg" -d "$RUNNER_TEMP/inspect-testing/nupkg"
# Its doc comments may point into Ageledger too, so the main package's net10.0 assembly is given as well.
mainnet="$RUNNER_TEMP/main-net"
rm -rf "$mainnet"
mkdir -p "$mainnet"
if ! unzip -q -o "$pkg" 'lib/net10.0/Ageledger.dll' -d "$mainnet" || [ ! -f "$mainnet/lib/net10.0/Ageledger.dll" ]; then
  echo "::error::lib/net10.0/Ageledger.dll is not in the main package, so the Ageledger.Testing doc links cannot be checked."
  fail=1
else
  dotnet run scripts/check-public-types.cs -- --crefs "$RUNNER_TEMP/inspect-testing/nupkg/lib/net10.0/Ageledger.Testing.xml" "$RUNNER_TEMP/inspect-testing/nupkg/lib/net10.0/Ageledger.Testing.dll" "$mainnet/lib/net10.0/Ageledger.dll" --allow-none || fail=1
fi
unzip -q "$tspkg" -d "$RUNNER_TEMP/inspect-testing/snupkg"
for mode in -rIl -rl; do
  leaks=$(grep -E $mode '/(Users|home)/' "$RUNNER_TEMP/inspect-testing" || true)
  if [ -n "$leaks" ]; then
    echo "::error::A machine path (/Users/ or /home/) is in the Ageledger.Testing package contents:"
    echo "$leaks"
    fail=1
  fi
done
dotnet run scripts/check-public-types.cs -- --pdb-paths "$RUNNER_TEMP/inspect-testing/snupkg" || fail=1
exit $fail
}

case "${1:-}" in
  main) [ $# -eq 2 ] || { echo "usage: $0 main <dir>" >&2; exit 2; }; check_main "$2" ;;
  testing) [ $# -eq 3 ] || { echo "usage: $0 testing <dir> <main package dir>" >&2; exit 2; }; check_testing "$2" "$3" ;;
  *) echo "usage: $0 main <dir> | testing <dir> <main package dir>" >&2; exit 2 ;;
esac
