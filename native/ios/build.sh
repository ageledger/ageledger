#!/bin/bash
# Builds AgeledgerNative.xcframework (device arm64 + simulator arm64) from Sources/AgeSignalsBridge.swift.
# Needs Xcode (swiftc, xcodebuild). The DeclaredAgeRange framework is weak-linked so the app still starts on iOS 15-25.
set -euo pipefail
cd "$(cd "$(dirname "$0")" && pwd -P)"   # the physical path, so -debug-prefix-map below matches what the compiler records even through a symlink
NAME=AgeledgerNative
MIN=15.0
OUT=build
REQUIRED_SDK=26.4   # the regulatory APIs (requiredRegulatoryFeatures, significant-update acknowledgment) need the iOS 26.4 SDK or newer

# Fail instead of building a framework that silently reports "not available": the SDK must contain DeclaredAgeRange at 26.4 or newer.
SDK_VERSION="$(xcrun --sdk iphoneos --show-sdk-version)"
if [ "$(printf '%s\n%s\n' "$REQUIRED_SDK" "$SDK_VERSION" | sort -V | head -n1)" != "$REQUIRED_SDK" ]; then
  echo "error: iPhoneOS SDK $SDK_VERSION is older than $REQUIRED_SDK; install an Xcode with the iOS $REQUIRED_SDK SDK or newer." >&2
  exit 1
fi
rm -rf "$OUT" "$NAME.xcframework"
mkdir -p "$OUT"

build_slice() { # <dir> <sdk> <target>
  local dir="$OUT/$1" sdk="$2" target="$3"
  local fw="$dir/$NAME.framework"
  mkdir -p "$fw/Headers"
  # Two steps so the object file survives for dsymutil (a one-step swiftc deletes its temporary object, leaving an empty dSYM).
  # Line tables only (enough to symbolicate crashes; keeps the dSYM small) plus -debug-prefix-map: the debug info must not carry this machine's folders (the package is checked for them),
  # so the source and build paths are written relative to the native/ios folder.
  # The SDK root is mapped under both its given and its resolved name (a symlinked Xcode, as on hosted runners, would otherwise leave the resolved path in).
  local sdk_path real_sdk_path
  sdk_path="$(xcrun --sdk "$sdk" --show-sdk-path)"
  real_sdk_path="$(cd "$sdk_path" && pwd -P)"
  local real_sdk_map=()
  if [ "$real_sdk_path" != "$sdk_path" ]; then real_sdk_map=(-debug-prefix-map "$real_sdk_path=/SDK"); fi
  xcrun --sdk "$sdk" swiftc -swift-version 5 -parse-as-library -O -gline-tables-only \
    -Xfrontend -no-clang-module-breadcrumbs \
    -debug-prefix-map "$PWD=." \
    -debug-prefix-map "$sdk_path=/SDK" ${real_sdk_map[@]+"${real_sdk_map[@]}"} \
    -module-name "$NAME" -target "$target" \
    -c -o "$dir/$NAME.o" \
    -emit-objc-header -emit-objc-header-path "$fw/Headers/$NAME-Swift.h" \
    Sources/AgeSignalsBridge.swift
  # -O on the link too: measured 2026-10-27, without it the linker stops folding identical code and __text grows.
  xcrun --sdk "$sdk" swiftc -O -module-name "$NAME" -target "$target" \
    -emit-library -o "$fw/$NAME" "$dir/$NAME.o" \
    -Xlinker -install_name -Xlinker "@rpath/$NAME.framework/$NAME" \
    -Xlinker -weak_framework -Xlinker DeclaredAgeRange
  # One dSYM per slice, shipped inside the xcframework so apps can symbolicate crashes in the wrapper.
  # Then strip the debug map (it names the object file's build folder) from the shipped binary.
  # Keep these paths relative (do not make them absolute): dsymutil records the binary's path in the dSYM's Relocations file.
  xcrun dsymutil "$fw/$NAME" -o "$dir/$NAME.framework.dSYM"
  xcrun strip -S "$fw/$NAME"
  cat > "$fw/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>$NAME</string>
<key>CFBundleIdentifier</key><string>com.ageledger.native</string>
<key>CFBundleName</key><string>$NAME</string>
<key>CFBundlePackageType</key><string>FMWK</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>MinimumOSVersion</key><string>$MIN</string>
</dict></plist>
PLIST
  # Sign last, once the bundle is complete, so the shipped bundle's signature matches its contents: the linker's own ad-hoc signature goes stale
  # when strip rewrites the binary and Info.plist is added. App builds with code signing on re-sign the framework anyway.
  codesign --force --sign - --timestamp=none "$fw"
}

build_slice device iphoneos "arm64-apple-ios$MIN"
build_slice simulator iphonesimulator "arm64-apple-ios$MIN-simulator"

xcodebuild -create-xcframework \
  -framework "$OUT/device/$NAME.framework" -debug-symbols "$PWD/$OUT/device/$NAME.framework.dSYM" \
  -framework "$OUT/simulator/$NAME.framework" -debug-symbols "$PWD/$OUT/simulator/$NAME.framework.dSYM" \
  -output "$NAME.xcframework"
echo "Built $(pwd)/$NAME.xcframework"
