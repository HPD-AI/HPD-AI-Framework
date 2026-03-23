# NuGet Test Project

This project is for testing HPD-Agent NuGet packages locally before publishing them to NuGet.org.

## Quick Start

### 1. Pack all packages locally

From this directory (`test/NuGetTest`), run:

```bash
./pack-local.sh 0.2.0
```

This will:
- Build all HPD-Agent packages
- Pack them with version 0.2.0
- Output to `./nuget-releases/0.2.0/`

### 2. Update nuget.config (if needed)

The `nuget.config` is already configured to use version 0.2.0. If you packed a different version, update the path:

```xml
<add key="Local" value="./nuget-releases/YOUR_VERSION" />
```

### 3. Add package references

```bash
cd test/NuGetTest

# Core packages
dotnet add package HPD-Agent -v 0.2.0
dotnet add package HPD-Agent.FFI -v 0.2.0
dotnet add package HPD-Agent.MCP -v 0.2.0
dotnet add package HPD-Agent.Memory -v 0.2.0
dotnet add package HPD-Agent.TextExtraction -v 0.2.0

# Toolkit packages
dotnet add package HPD-Agent.Toolkit.FileSystem -v 0.2.0
dotnet add package HPD-Agent.Toolkit.WebSearch -v 0.2.0

# Provider packages
dotnet add package HPD-Agent.Providers.Anthropic -v 0.2.0
dotnet add package HPD-Agent.Providers.OpenAI -v 0.2.0
# ... add any other providers you want to test

# Events package
dotnet add package HPD.Events -v 0.2.0
```

### 4. Force restore to use local packages

```bash
dotnet restore --force
```

### 5. Write test code

Edit `Program.cs` to test the packages:

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Anthropic;

var agent = new AgentBuilder()
    .WithAnthropicProvider("your-api-key")
    .Build();

// Test your code...
```

### 6. Run the test

```bash
dotnet run
```

## All Available Packages

The following packages will be packed (same as the publish workflow):

**Core:**
- HPD-Agent
- HPD-Agent.FFI
- HPD-Agent.MCP
- HPD-Agent.Memory
- HPD-Agent.TextExtraction

**Toolkits:**
- HPD-Agent.Toolkit.FileSystem
- HPD-Agent.Toolkit.WebSearch

**Providers:**
- HPD-Agent.Providers.Anthropic
- HPD-Agent.Providers.AzureAI
- HPD-Agent.Providers.AzureAIInference
- HPD-Agent.Providers.Bedrock
- HPD-Agent.Providers.GoogleAI
- HPD-Agent.Providers.HuggingFace
- HPD-Agent.Providers.Mistral
- HPD-Agent.Providers.Ollama
- HPD-Agent.Providers.OnnxRuntime
- HPD-Agent.Providers.OpenAI
- HPD-Agent.Providers.OpenRouter

**Events:**
- HPD.Events

## Folder Structure

```
test/NuGetTest/
├── pack-local.sh           # Build script to pack all packages
├── nuget.config            # NuGet configuration (points to local packages)
├── nuget-releases/         # Local package releases (gitignored)
│   ├── 0.2.0/             # Version-specific folder
│   │   ├── HPD-Agent.0.2.0.nupkg
│   │   ├── HPD-Agent.0.2.0.snupkg
│   │   └── ... (all other packages)
│   └── 0.3.0/             # You can have multiple versions
├── NuGetTest.csproj
├── Program.cs
└── README.md
```

## Configuration

The `nuget.config` file configures NuGet to:
1. **First** look in `./nuget-releases/VERSION/` for local packages
2. **Fall back** to nuget.org for dependencies

## Tips

- **Multiple versions:** You can pack multiple versions side-by-side in different folders
- **Clean builds:** To rebuild, just run `./pack-local.sh VERSION` again
- **Version testing:** Test different versions by updating the path in `nuget.config`
- **All packages ignored:** The `.nupkg` and `.snupkg` files in `nuget-releases/` are gitignored

## Notes

- Always test packages locally before publishing to NuGet.org
- The pack script uses the same project list as the GitHub Actions publish workflow
- Symbol packages (`.snupkg`) are also generated for debugging
