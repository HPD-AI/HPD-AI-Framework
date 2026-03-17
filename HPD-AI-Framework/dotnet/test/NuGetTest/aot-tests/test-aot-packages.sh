#!/bin/bash

# HPD-Agent Native AOT Package Testing Script
# This script tests each package individually to identify AOT compatibility issues

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_ROOT="$SCRIPT_DIR"
RESULTS_FILE="$TEST_ROOT/aot-test-results.txt"
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "================================================"
echo "HPD-Agent Native AOT Compatibility Test"
echo "================================================"
echo "Started: $TIMESTAMP"
echo ""

# Clear previous results
echo "Test Results - $TIMESTAMP" > "$RESULTS_FILE"
echo "================================================" >> "$RESULTS_FILE"
echo "" >> "$RESULTS_FILE"

# Package list
PACKAGES=(
    "HPD.Events"
    "HPD-Agent.TextExtraction"
    "HPD-Agent.Framework"
    "HPD-Agent.FFI"
    "HPD-Agent.MCP"
    "HPD-Agent.Memory"
    "HPD-Agent.Toolkit.FileSystem"
    "HPD-Agent.Toolkit.WebSearch"
    "HPD-Agent.Providers.Anthropic"
    "HPD-Agent.Providers.OpenAI"
    "HPD-Agent.Providers.Bedrock"
    "HPD-Agent.Providers.GoogleAI"
    "HPD-Agent.Providers.Ollama"
    "HPD-Agent.Providers.HuggingFace"
    "HPD-Agent.Providers.Mistral"
)

# Function to create minimal test project
create_test_project() {
    local package_name=$1
    local project_dir="$TEST_ROOT/test-$package_name"

    echo -e "${BLUE}Creating test project for: $package_name${NC}"

    # Clean up old project
    rm -rf "$project_dir"
    mkdir -p "$project_dir"

    # Create .csproj file
    cat > "$project_dir/Test.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>false</InvariantGlobalization>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <SuppressTrimAnalysisWarnings>false</SuppressTrimAnalysisWarnings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="$package_name" Version="0.2.0" />
  </ItemGroup>
</Project>
EOF

    # Create minimal Program.cs
    cat > "$project_dir/Program.cs" << 'EOF'
Console.WriteLine("Testing package load...");
Console.WriteLine("Package loaded successfully!");
EOF

    echo "$project_dir"
}

# Function to test a package
test_package() {
    local package_name=$1

    echo -e "${YELLOW}Testing: $package_name${NC}"
    echo "----------------------------------------"

    # Create project and get directory
    local project_dir="$TEST_ROOT/test-$package_name"
    create_test_project "$package_name"

    local output_file="$project_dir/build-output.txt"
    local error_file="$project_dir/build-errors.txt"

    # Attempt to publish with Native AOT
    if dotnet publish "$project_dir/Test.csproj" -c Release > "$output_file" 2> "$error_file"; then
        # Check if native executable was created
        local publish_dir="$project_dir/bin/Release/net8.0/osx-arm64/publish"

        if [ -f "$publish_dir/Test" ] && [ ! -f "$publish_dir/Test.dll" ]; then
            echo -e "  ${GREEN} NATIVE AOT: Success - Native executable generated${NC}"
            echo "$package_name:  NATIVE AOT SUCCESS" >> "$RESULTS_FILE"
        elif [ -f "$publish_dir/Test.dll" ]; then
            echo -e "  ${YELLOW}  AOT FALLBACK: Built as .NET assembly (not native)${NC}"
            echo "$package_name:   AOT FALLBACK (DLL only)" >> "$RESULTS_FILE"

            # Extract trim warnings
            if grep -q "IL[0-9]\{4\}" "$output_file" "$error_file" 2>/dev/null; then
                echo "  Trim warnings found:" >> "$RESULTS_FILE"
                grep -o "IL[0-9]\{4\}" "$output_file" "$error_file" 2>/dev/null | sort | uniq -c >> "$RESULTS_FILE"
            fi
        else
            echo -e "  ${RED} BUILD FAILED: No output generated${NC}"
            echo "$package_name:  BUILD FAILED" >> "$RESULTS_FILE"
        fi

        # Check for IL warnings (trim/AOT issues)
        local warning_count=$(cat "$output_file" "$error_file" 2>/dev/null | grep -c "IL[0-9]\{4\}" || echo "0")
        if [ "$warning_count" -gt 0 ]; then
            echo -e "  ${YELLOW}Found $warning_count trim/AOT warnings${NC}"
        fi
    else
        echo -e "  ${RED} BUILD ERROR${NC}"
        echo "$package_name:  BUILD ERROR" >> "$RESULTS_FILE"
        echo "  Error details:" >> "$RESULTS_FILE"
        tail -20 "$error_file" >> "$RESULTS_FILE" 2>/dev/null || echo "  No error details" >> "$RESULTS_FILE"
    fi

    echo "" >> "$RESULTS_FILE"
    echo ""
}

# Test each package
for package in "${PACKAGES[@]}"; do
    test_package "$package"
done

echo "================================================"
echo "Testing Complete!"
echo "================================================"
echo ""
echo "Results saved to: $RESULTS_FILE"
echo ""
echo -e "${BLUE}Summary:${NC}"
grep "\|\|" "$RESULTS_FILE" | sort

echo ""
echo "Detailed results available in: $RESULTS_FILE"
echo ""
echo "To view individual build logs, check:"
echo "  $TEST_ROOT/test-*/build-*.txt"
