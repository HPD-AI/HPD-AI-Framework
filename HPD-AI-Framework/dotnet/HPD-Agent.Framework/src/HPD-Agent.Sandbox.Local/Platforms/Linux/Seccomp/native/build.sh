#!/bin/bash
# Build script for HPD Sandbox seccomp helpers
# Run this during package build, not at runtime

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_DIR="${SCRIPT_DIR}/bin"

mkdir -p "$OUTPUT_DIR"

echo "Building seccomp helpers..."

# Build x64 version
if command -v gcc &> /dev/null; then
    echo "Building x64 helper..."

    # Try static linking first (more portable)
    if gcc -O2 -static -o "${OUTPUT_DIR}/apply-seccomp-x64" "${SCRIPT_DIR}/apply-seccomp-x64.c" 2>/dev/null; then
        echo "  Built with static linking"
    else
        # Fall back to dynamic linking
        gcc -O2 -o "${OUTPUT_DIR}/apply-seccomp-x64" "${SCRIPT_DIR}/apply-seccomp-x64.c"
        echo "  Built with dynamic linking"
    fi

    chmod +x "${OUTPUT_DIR}/apply-seccomp-x64"
    echo "  -> ${OUTPUT_DIR}/apply-seccomp-x64"
fi

# Build ARM64 version (cross-compile or native)
if command -v aarch64-linux-gnu-gcc &> /dev/null; then
    echo "Building ARM64 helper (cross-compile)..."
    aarch64-linux-gnu-gcc -O2 -static -o "${OUTPUT_DIR}/apply-seccomp-arm64" "${SCRIPT_DIR}/apply-seccomp-arm64.c" 2>/dev/null || \
    aarch64-linux-gnu-gcc -O2 -o "${OUTPUT_DIR}/apply-seccomp-arm64" "${SCRIPT_DIR}/apply-seccomp-arm64.c"
    chmod +x "${OUTPUT_DIR}/apply-seccomp-arm64"
    echo "  -> ${OUTPUT_DIR}/apply-seccomp-arm64"
elif [[ "$(uname -m)" == "aarch64" ]] && command -v gcc &> /dev/null; then
    echo "Building ARM64 helper (native)..."
    gcc -O2 -static -o "${OUTPUT_DIR}/apply-seccomp-arm64" "${SCRIPT_DIR}/apply-seccomp-arm64.c" 2>/dev/null || \
    gcc -O2 -o "${OUTPUT_DIR}/apply-seccomp-arm64" "${SCRIPT_DIR}/apply-seccomp-arm64.c"
    chmod +x "${OUTPUT_DIR}/apply-seccomp-arm64"
    echo "  -> ${OUTPUT_DIR}/apply-seccomp-arm64"
else
    echo "Skipping ARM64 (no cross-compiler available)"
fi

echo ""
echo "Build complete. Files:"
ls -la "${OUTPUT_DIR}/"

echo ""
echo "To include in NuGet package, add to .csproj:"
echo ""
cat << 'EOF'
  <ItemGroup Condition="$([MSBuild]::IsOSPlatform('Linux'))">
    <Content Include="Platforms/Linux/Seccomp/native/bin/apply-seccomp-*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <PackagePath>runtimes/linux-x64/native;runtimes/linux-arm64/native</PackagePath>
    </Content>
  </ItemGroup>
EOF
