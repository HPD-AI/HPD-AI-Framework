#!/usr/bin/env bash
# Build HpdRecorder.xcframework from HpdRecorder.xcodeproj
# Output: libHpdRecorder.xcframework/  (checked into git, bundled by HPDOS.Shell.csproj)
#
# Run from this directory:
#   ./build-xcframework.sh
#
# Requires Xcode command-line tools installed.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ="$SCRIPT_DIR/HpdRecorder.xcodeproj"
BUILD="$SCRIPT_DIR/.build-xcframework"
OUTPUT="$SCRIPT_DIR/libHpdRecorder.xcframework"

echo "==> Cleaning build dir"
rm -rf "$BUILD" "$OUTPUT"
mkdir -p "$BUILD"

echo "==> Building for maccatalyst-arm64 (Release)"
xcodebuild archive \
  -project "$PROJ" \
  -scheme HpdRecorder \
  -configuration Release \
  -destination "generic/platform=macOS,variant=Mac Catalyst" \
  -archivePath "$BUILD/HpdRecorder-maccatalyst.xcarchive" \
  SKIP_INSTALL=NO \
  BUILD_LIBRARY_FOR_DISTRIBUTION=YES \
  SUPPORTS_MACCATALYST=YES \
  MACH_O_TYPE=staticlib \
  | xcpretty || true

echo "==> Creating xcframework"
xcodebuild -create-xcframework \
  -framework "$BUILD/HpdRecorder-maccatalyst.xcarchive/Products/Library/Frameworks/HpdRecorder.framework" \
  -output "$OUTPUT"

echo "==> Done: $OUTPUT"
echo "    Check this directory into git."
