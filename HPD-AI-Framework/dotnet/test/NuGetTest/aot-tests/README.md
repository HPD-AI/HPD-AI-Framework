# Native AOT Package Testing

This directory contains tools to test individual HPD-Agent packages for Native AOT compatibility.

## Purpose

The `test-aot-packages.sh` script creates isolated test projects for each HPD-Agent package and attempts to compile them with Native AOT enabled. This helps identify:

1. Which packages are Native AOT compatible
2. Which packages cause AOT compilation to fall back to standard .NET
3. Specific trim warnings (IL2XXX, IL3XXX) from each package
4. Build errors specific to AOT compilation

## Usage

### Run All Tests

```bash
cd test/NuGetTest/aot-tests
./test-aot-packages.sh
```

### View Results

Results are saved to `aot-test-results.txt` with:
-  Native AOT Success - Package compiled to native executable
-   AOT Fallback - Package compiled but fell back to .NET assembly
-  Build Error - Package failed to compile

Individual build logs are saved in `test-<package-name>/build-*.txt`

## Tested Packages

The script tests these HPD-Agent packages individually:

**Core Packages:**
- HPD.Events
- HPD-Agent.TextExtraction
- HPD-Agent.Framework
- HPD-Agent.FFI
- HPD-Agent.MCP
- HPD-Agent.Memory

**Toolkits:**
- HPD-Agent.Toolkit.FileSystem
- HPD-Agent.Toolkit.WebSearch

**AI Providers:**
- HPD-Agent.Providers.Anthropic
- HPD-Agent.Providers.OpenAI
- HPD-Agent.Providers.Bedrock
- HPD-Agent.Providers.GoogleAI
- HPD-Agent.Providers.Ollama
- HPD-Agent.Providers.HuggingFace
- HPD-Agent.Providers.Mistral

## Understanding Results

### Native AOT Success ()
The package and all its dependencies are fully AOT-compatible. A native executable was generated with no .dll fallback.

### AOT Fallback ()
The package compiled but .NET couldn't produce a native executable. This happens when:
- The package uses reflection that can't be statically analyzed
- Dependencies include non-AOT-compatible libraries
- Runtime code generation is required

Common warnings include:
- **IL2026**: Reflection/trimming issues - code uses reflection that may break when trimmed
- **IL3050**: Dynamic code generation - code requires runtime compilation
- **IL2057**: Unrecognized types passed to Type.GetType()
- **IL2087**: Data flow analysis issues with reflection

### Build Error ()
The package failed to compile entirely, indicating:
- Missing dependencies
- Platform-specific issues
- Critical AOT compatibility problems

## Output Structure

```
aot-tests/
├── test-aot-packages.sh          # Main test script
├── README.md                      # This file
├── aot-test-results.txt          # Summary results (generated)
├── test-HPD.Events/              # Generated test project (gitignored)
│   ├── Test.csproj
│   ├── Program.cs
│   ├── build-output.txt
│   └── build-errors.txt
├── test-HPD-Agent.Framework/     # Generated test project (gitignored)
│   └── ...
└── ...
```

All `test-*/` directories and `*.txt` result files are automatically gitignored.

## Interpreting for Production

**For Library Authors:**
If your package shows AOT Fallback or errors, consider:
- Using source generators instead of reflection
- Adding trim annotations ([DynamicallyAccessedMembers], [RequiresUnreferencedCode])
- Providing AOT-compatible alternatives for reflection-heavy features
- Documenting AOT limitations

**For Application Developers:**
If critical packages aren't AOT-compatible:
- Use standard .NET deployment (fully functional, no limitations)
- Use ReadyToRun compilation for faster startup without full AOT
- Use Self-Contained deployment for standalone executables
- Wait for upstream dependencies to add AOT support

## CI/CD Integration

To integrate into continuous testing:

```bash
# Run tests and exit with failure if any package fails to build
./test-aot-packages.sh
if grep -q "" aot-test-results.txt; then
    echo "Some packages failed AOT testing"
    exit 1
fi
```

## Future Work

As the .NET ecosystem improves AOT support:
- Run this script periodically to track progress
- Update package versions and retest
- Contribute AOT fixes to upstream dependencies
- Share results with the HPD-Agent community
