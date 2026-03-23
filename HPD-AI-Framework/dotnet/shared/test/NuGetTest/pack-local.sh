#!/bin/bash
set -e

# Script to pack all HPD-Agent NuGet packages locally for testing
# Usage: ./pack-local.sh <version>
# Example: ./pack-local.sh 0.2.0

# Check if version argument is provided
if [ -z "$1" ]; then
  echo "Error: Version argument is required"
  echo ""
  echo "Usage: ./pack-local.sh <version>"
  echo "Example: ./pack-local.sh 0.2.0"
  echo "Example: ./pack-local.sh 0.3.0-alpha"
  echo ""
  exit 1
fi

VERSION=$1

# Validate version format (MAJOR.MINOR.PATCH with optional suffix)
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.-]+)?$ ]]; then
  echo "Error: Invalid version format"
  echo ""
  echo "Version must follow semantic versioning: MAJOR.MINOR.PATCH"
  echo "Examples:"
  echo "  ✓ 0.2.0"
  echo "  ✓ 1.0.0"
  echo "  ✓ 0.3.0-alpha"
  echo "  ✓ 1.2.3-beta.1"
  echo "  ✗ 0.20 (missing patch version)"
  echo "  ✗ 1.0 (missing patch version)"
  echo ""
  exit 1
fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/nuget-releases/$VERSION"

echo "=========================================="
echo "Packing HPD-Agent NuGet Packages"
echo "=========================================="
echo "Version: $VERSION"
echo "Output: $OUTPUT_DIR"
echo "=========================================="

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Clean previous builds in output directory
rm -f "$OUTPUT_DIR"/*.nupkg

# Array of all projects to pack (same as publish workflow)
PROJECTS=(
  "HPD-Agent/HPD-Agent.csproj"
  "HPD-Agent.FFI/HPD-Agent.FFI.csproj"
  "HPD-Agent.MCP/HPD-Agent.MCP.csproj"
  "HPD-Agent.Memory/HPD-Agent.Memory.csproj"
  "HPD-Agent.TextExtraction/HPD-Agent.TextExtraction.csproj"
  "HPD-Agent.Toolkit/HPD-Agent.Toolkit.FileSystem/HPD-Agent.Toolkit.FileSystem.csproj"
  "HPD-Agent.Toolkit/HPD-Agent.Toolkit.WebSearch/HPD-Agent.Toolkit.WebSearch.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.Anthropic/HPD-Agent.Providers.Anthropic.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.AzureAI/HPD-Agent.Providers.AzureAI.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.AzureAIInference/HPD-Agent.Providers.AzureAIInference.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.Bedrock/HPD-Agent.Providers.Bedrock.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.GoogleAI/HPD-Agent.Providers.GoogleAI.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.HuggingFace/HPD-Agent.Providers.HuggingFace.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.Mistral/HPD-Agent.Providers.Mistral.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.Ollama/HPD-Agent.Providers.Ollama.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.OnnxRuntime/HPD-Agent.Providers.OnnxRuntime.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.OpenAI/HPD-Agent.Providers.OpenAI.csproj"
  "HPD-Agent.Providers/HPD-Agent.Providers.OpenRouter/HPD-Agent.Providers.OpenRouter.csproj"
  "HPD.Events/HPD.Events.csproj"
)

# Parse version into prefix and suffix
if [[ "$VERSION" == *-* ]]; then
  VERSION_PREFIX="${VERSION%%-*}"
  VERSION_SUFFIX="${VERSION#*-}"
  echo "Version prefix: $VERSION_PREFIX"
  echo "Version suffix: $VERSION_SUFFIX"
else
  VERSION_PREFIX="$VERSION"
  VERSION_SUFFIX=""
  echo "Stable version: $VERSION_PREFIX"
fi

echo ""
echo "Building and packing packages..."
echo ""

# Pack each project
for PROJECT in "${PROJECTS[@]}"; do
  PROJECT_PATH="$REPO_ROOT/$PROJECT"
  PROJECT_NAME=$(basename "$PROJECT" .csproj)

  if [ ! -f "$PROJECT_PATH" ]; then
    echo "  SKIP: $PROJECT_NAME (project file not found)"
    continue
  fi

  echo "📦 Packing: $PROJECT_NAME..."

  if [ -n "$VERSION_SUFFIX" ]; then
    dotnet pack "$PROJECT_PATH" \
      -c Release \
      -o "$OUTPUT_DIR" \
      -p:VersionPrefix="$VERSION_PREFIX" \
      -p:VersionSuffix="$VERSION_SUFFIX" \
      --no-restore
  else
    dotnet pack "$PROJECT_PATH" \
      -c Release \
      -o "$OUTPUT_DIR" \
      -p:VersionPrefix="$VERSION_PREFIX" \
      --no-restore
  fi

  if [ $? -eq 0 ]; then
    echo " $PROJECT_NAME"
  else
    echo " FAILED: $PROJECT_NAME"
  fi
  echo ""
done

echo "=========================================="
echo "Packing Complete!"
echo "=========================================="
echo ""
echo "Packages created in: $OUTPUT_DIR"
echo ""
echo "Package count:"
ls -1 "$OUTPUT_DIR"/*.nupkg 2>/dev/null | wc -l
echo ""
echo "To use these packages in the test project:"
echo "1. Update nuget.config to point to: $OUTPUT_DIR"
echo "2. Run: dotnet restore --force"
echo "3. Add package references with version $VERSION"
echo ""
