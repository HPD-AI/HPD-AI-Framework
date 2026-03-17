#!/usr/bin/env bash
# publish.sh — Cross-platform CLI publisher for HPDOS
# Publishes HPDOS.CLI for all supported platforms and organizes into releases/ directory
#
# Usage:
#   ./scripts/publish.sh [version]           # publish all platforms
#   ./scripts/publish.sh [version] --dry-run # show what would be published
#   ./scripts/publish.sh [version] linux-x64 # publish single platform

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CLI_PROJECT="$PROJECT_ROOT/src-dotnet/HPDOS.CLI/HPDOS.CLI.csproj"

# Default version from git tag or package.json fallback
VERSION="${1:-}"
DRY_RUN=false
SINGLE_PLATFORM="${2:-}"

if [[ -z "$VERSION" ]]; then
  VERSION=$(git -C "$PROJECT_ROOT" describe --tags --abbrev=0 2>/dev/null || echo "dev")
fi

if [[ "$SINGLE_PLATFORM" == "--dry-run" ]]; then
  DRY_RUN=true
fi

RELEASE_DIR="$PROJECT_ROOT/releases/$VERSION"
BUILD_LOG="$PROJECT_ROOT/build.log"

# Determine buildable platforms based on current OS
UNAME=$(uname -s)
case "$UNAME" in
  Darwin)
    # macOS can only build for macOS natively
    PLATFORMS=(
      "osx-x64:hpdos-$VERSION-darwin-x64"
      "osx-arm64:hpdos-$VERSION-darwin-arm64"
    )
    NOTE=" (other platforms require CI/CD or Docker)"
    ;;
  Linux)
    # Linux can build for Linux natively, Windows via mingw (if available)
    PLATFORMS=(
      "linux-x64:hpdos-$VERSION-linux-x64"
      "linux-arm64:hpdos-$VERSION-linux-arm64"
    )
    # Check if Windows cross-compilation is available
    if command -v x86_64-w64-mingw32-gcc &> /dev/null; then
      PLATFORMS+=(
        "win-x64:hpdos-$VERSION-windows-x64.exe"
      )
    fi
    NOTE=" (Windows RID requires native Windows or CI/CD)"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    # Windows can build for Windows natively
    PLATFORMS=(
      "win-x64:hpdos-$VERSION-windows-x64.exe"
      "win-arm64:hpdos-$VERSION-windows-arm64.exe"
    )
    NOTE=" (other platforms require CI/CD or WSL)"
    ;;
  *)
    echo "⚠️  Unknown OS: $UNAME"
    exit 1
    ;;
esac

# Filter to single platform if specified
if [[ -n "$SINGLE_PLATFORM" && "$SINGLE_PLATFORM" != "--dry-run" ]]; then
  PLATFORMS=( $(for p in "${PLATFORMS[@]}"; do
    if [[ "$p" == "$SINGLE_PLATFORM"* ]]; then
      echo "$p"
    fi
  done) )
fi

echo "🔨 HPDOS CLI Publisher"
echo "  Version:  $VERSION"
echo "  Release:  $RELEASE_DIR"
echo "  OS:       $UNAME"
echo "  Platforms: ${#PLATFORMS[@]}$NOTE"
echo ""

if [[ "$DRY_RUN" == true ]]; then
  echo "📋 DRY RUN — Files that would be created:"
  for platform in "${PLATFORMS[@]}"; do
    rid="${platform%%:*}"
    filename="${platform##*:}"
    echo "  ✓ $RELEASE_DIR/$filename"
  done
  exit 0
fi

# Create release directory
mkdir -p "$RELEASE_DIR"
echo "📁 Created $RELEASE_DIR"
echo ""

# Counter
total=${#PLATFORMS[@]}
current=0

# Publish each platform
for platform in "${PLATFORMS[@]}"; do
  rid="${platform%%:*}"
  filename="${platform##*:}"
  
  ((current += 1))
  echo "[$current/$total] 🔨 Building $rid..."
  
  output="$RELEASE_DIR/$filename"
  
  # Publish as self-contained executable
  # Note: AOT disabled to support source generators in dependencies
  if dotnet publish "$CLI_PROJECT" \
    --configuration Release \
    --runtime "$rid" \
    --output "$RELEASE_DIR/temp-$rid" \
    --self-contained \
    --no-restore \
    >> "$BUILD_LOG" 2>&1; then
    
    # Find the binary (hpdos or hpdos.exe)
    if [[ "$rid" == "win"* ]]; then
      src_binary="$RELEASE_DIR/temp-$rid/hpdos.exe"
    else
      src_binary="$RELEASE_DIR/temp-$rid/hpdos"
    fi
    
    if [[ -f "$src_binary" ]]; then
      mv "$src_binary" "$output"
      chmod +x "$output"
      
      # Get file size
      size=$(du -h "$output" | cut -f1)
      echo "  ✓ $filename ($size)"
    else
      echo "  ✗ ERROR: Binary not found at $src_binary"
      exit 1
    fi
    
    # Cleanup temp directory
    rm -rf "$RELEASE_DIR/temp-$rid"
  else
    echo "  ✗ ERROR: Build failed for $rid (check build.log)"
    exit 1
  fi
done

echo ""
echo "✅ All platforms published to: $RELEASE_DIR"
echo ""

# Generate checksums
echo "🔐 Generating checksums..."
cd "$RELEASE_DIR"
if command -v shasum &> /dev/null; then
  shasum -a 256 * > CHECKSUMS.txt && echo "✓ CHECKSUMS.txt"
elif command -v sha256sum &> /dev/null; then
  sha256sum * > CHECKSUMS.txt && echo "✓ CHECKSUMS.txt"
fi

echo ""
echo "📦 Release package ready!"
echo "   $RELEASE_DIR/"
echo ""
echo "To distribute:"
echo "  • Upload to GitHub Releases"
echo "  • Create platform-specific installers (optional)"
echo "  • Update download links in documentation"
