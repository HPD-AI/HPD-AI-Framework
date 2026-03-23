#!/bin/bash

# HPD-Agent Native AOT Local Project Testing Script
# This script tests local projects (not NuGet packages) for AOT compatibility

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_ROOT="$SCRIPT_DIR"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
RESULTS_FILE="$TEST_ROOT/aot-local-test-results.txt"
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "================================================"
echo "HPD-Agent Native AOT Compatibility Test (Local)"
echo "================================================"
echo "Started: $TIMESTAMP"
echo "Repository: $REPO_ROOT"
echo ""

# Clear previous results
echo "Test Results - $TIMESTAMP" > "$RESULTS_FILE"
echo "================================================" >> "$RESULTS_FILE"
echo "" >> "$RESULTS_FILE"

# Function to create minimal test project with local project reference
create_test_project() {
    local package_name=$1
    local project_path=$2
    local project_dir="$TEST_ROOT/local-test-$package_name"
    local full_project_path="$REPO_ROOT/$project_path"

    echo -e "${BLUE}Creating test project for: $package_name${NC}"
    echo "  Project: $project_path"

    # Clean up old project
    rm -rf "$project_dir"
    mkdir -p "$project_dir"

    # Create .csproj file with ProjectReference instead of PackageReference
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
    <ProjectReference Include="$full_project_path" />
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
    local project_path=$2

    echo -e "${YELLOW}Testing: $package_name${NC}"
    echo "----------------------------------------"

    # Create project and get directory
    local project_dir="$TEST_ROOT/local-test-$package_name"
    create_test_project "$package_name" "$project_path"

    local output_file="$project_dir/build-output.txt"
    local error_file="$project_dir/build-errors.txt"

    # Attempt to publish with Native AOT
    if dotnet publish "$project_dir/Test.csproj" -c Release -r osx-arm64 > "$output_file" 2> "$error_file"; then
        # Check for IL warnings (the real AOT compatibility test)
        local warning_count=$(grep -o "IL[0-9]\{4\}" "$output_file" "$error_file" 2>/dev/null | wc -l)

        if [ "$warning_count" -eq 0 ]; then
            echo -e "  ${GREEN} AOT COMPATIBLE: 0 IL warnings${NC}"
            echo "$package_name:  AOT COMPATIBLE (0 warnings)" >> "$RESULTS_FILE"
        else
            echo -e "  ${RED} AOT INCOMPATIBLE: $warning_count IL warnings${NC}"
            echo "$package_name:  AOT INCOMPATIBLE ($warning_count warnings)" >> "$RESULTS_FILE"
            echo "  Warning breakdown:" >> "$RESULTS_FILE"
            grep -o "IL[0-9]\{4\}" "$output_file" "$error_file" 2>/dev/null | sort | uniq -c >> "$RESULTS_FILE"
        fi

        # Also check if native executable was created (informational only)
        local publish_dir="$project_dir/bin/Release/net8.0/osx-arm64/publish"
        if [ -f "$publish_dir/Test" ] && [ ! -f "$publish_dir/Test.dll" ]; then
            echo -e "  ${BLUE}ℹ️  Native executable generated${NC}"
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

# Test each package (simple array approach for compatibility)
test_package "HPD.Events" "HPD.Events/HPD.Events.csproj"
test_package "HPD-Agent.TextExtraction" "HPD-Agent.TextExtraction/HPD-Agent.TextExtraction.csproj"
test_package "HPD-Agent.Framework" "HPD-Agent/HPD-Agent.csproj"
test_package "HPD-Agent.FFI" "HPD-Agent.FFI/HPD-Agent.FFI.csproj"
test_package "HPD-Agent.MCP" "HPD-Agent.MCP/HPD-Agent.MCP.csproj"
test_package "HPD-Agent.Memory" "HPD-Agent.Memory/HPD-Agent.Memory.csproj"
test_package "HPD-Agent.Toolkit.FileSystem" "HPD-Agent.Toolkit/HPD-Agent.Toolkit.FileSystem/HPD-Agent.Toolkit.FileSystem.csproj"
test_package "HPD-Agent.Toolkit.WebSearch" "HPD-Agent.Toolkit/HPD-Agent.Toolkit.WebSearch/HPD-Agent.Toolkit.WebSearch.csproj"
test_package "HPD-Agent.Providers.Anthropic" "HPD-Agent.Providers/HPD-Agent.Providers.Anthropic/HPD-Agent.Providers.Anthropic.csproj"
test_package "HPD-Agent.Providers.OpenAI" "HPD-Agent.Providers/HPD-Agent.Providers.OpenAI/HPD-Agent.Providers.OpenAI.csproj"
test_package "HPD-Agent.Providers.Bedrock" "HPD-Agent.Providers/HPD-Agent.Providers.Bedrock/HPD-Agent.Providers.Bedrock.csproj"
test_package "HPD-Agent.Providers.GoogleAI" "HPD-Agent.Providers/HPD-Agent.Providers.GoogleAI/HPD-Agent.Providers.GoogleAI.csproj"
test_package "HPD-Agent.Providers.Ollama" "HPD-Agent.Providers/HPD-Agent.Providers.Ollama/HPD-Agent.Providers.Ollama.csproj"
test_package "HPD-Agent.Providers.HuggingFace" "HPD-Agent.Providers/HPD-Agent.Providers.HuggingFace/HPD-Agent.Providers.HuggingFace.csproj"
test_package "HPD-Agent.Providers.Mistral" "HPD-Agent.Providers/HPD-Agent.Providers.Mistral/HPD-Agent.Providers.Mistral.csproj"

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
echo "  $TEST_ROOT/local-test-*/build-*.txt"
