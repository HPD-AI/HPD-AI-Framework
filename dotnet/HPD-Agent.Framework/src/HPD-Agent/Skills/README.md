# Skills

Skills provide task-specific instructions and child capabilities without placing
the full workflow in every model request. Before activation, the model sees only
the skill name and discovery description. Activation returns the instructions
and reveals the skill's functions, resources, and scripts for the remainder of
the current message turn.

## Code-first skills

Declare a skill on the tool harness that owns it:

```csharp
using HPD.Agent;

public sealed partial class DataTools
{
    [AIFunction(Name = "validate_dataset")]
    [AIDescription("Validates the active dataset and reports problems.")]
    public string ValidateDataset() => "Dataset is valid.";

    [Skill]
    public static Skill DataAnalysis() => Skill.Create(
        name: "data_analysis",
        description: "Validates and analyzes datasets.",
        instructions: SkillInstructions.FromText("""
            Validate the dataset before analysis.
            Explain any validation failures.
            """),
        capabilities:
        [
            SkillCapabilities.Function<DataTools>(nameof(ValidateDataset)),
            SkillCapabilities.Resource(
                "read_validation_guide",
                "Reads the dataset validation requirements.",
                "Every row must have a unique identifier.")
        ]);
}
```

Register the harness normally:

```csharp
await using var agent = await new AgentBuilder(new AgentConfig { Name = "data-agent" })
    .WithToolHarness<DataTools>()
    .BuildAsync();
```

`Skill.Create` and `SkillCapabilities.Script` are the validated construction
paths. Skill names and child capability names must contain 1-64 ASCII letters,
digits, underscores, or hyphens, and must start with a letter or underscore.

## SKILL.md directories

`DirectorySkillSource` accepts one skill directory or a parent containing
several skill directories:

```text
skills/
└── data-analysis/
    ├── SKILL.md
    ├── skill.json
    ├── references/
    │   └── validation-guide.md
    └── scripts/
        └── normalize.py
```

```csharp
var skills = new DirectorySkillSource(
    "./skills",
    SkillDirectoryImportMode.Strict);

await using var agent = await new AgentBuilder(new AgentConfig { Name = "data-agent" })
    .WithToolHarness<DataTools>(options => options.AddSkillSource(skills))
    .BuildAsync();
```

`SKILL.md` requires YAML frontmatter and a non-empty instruction body:

```markdown
---
id: data-analysis
name: data_analysis
description: Validates and analyzes datasets.
version: 1
---

Read the validation guide first.
Validate the dataset before analysis.
```

Strict mode requires every packaged resource and script to have a description
in `skill.json`:

```json
{
  "resources": {
    "references/validation-guide.md": "Reads the dataset validation requirements."
  },
  "scripts": {
    "scripts/normalize.py": {
      "description": "Normalizes the active dataset and returns the resulting artifact.",
      "runtime": "python",
      "requiresPermission": true,
      "parameters": {
        "type": "object",
        "properties": {
          "inputFile": { "type": "string" },
          "maximumRows": {
            "type": "integer",
            "minimum": 1,
            "default": 1000
          }
        },
        "required": ["inputFile"],
        "additionalProperties": false
      }
    }
  }
}
```

Every script requires an explicit `parameters` object, including scripts with no
arguments. Importing a directory never executes its scripts. Directory changes
are watched, and invalid edits retain the last valid capability epoch.

References, scripts, metadata files, and contract sidecars must remain beneath
the skill root. Absolute paths, traversal, remote contracts, and symbolic links
are rejected.

## Managed skill store

Use managed skills when packages are installed, updated, or removed without
recompiling the application:

```csharp
var services = new ServiceCollection();
services.AddHPDSkills();
using var provider = services.BuildServiceProvider();

var store = provider.GetRequiredService<ISkillStore>();
await store.InstallAsync(new SkillPackage
{
    Manifest = new SkillPackageManifest
    {
        Id = "data-analysis",
        Name = "data_analysis",
        Description = "Validates and analyzes datasets.",
        Version = "1",
        Tags = new Dictionary<string, string> { ["domain"] = "data" }
    },
    Instructions = File.OpenRead("instructions.md")
});
```

Attach selected packages to a harness:

```csharp
var builder = new AgentBuilder(new AgentConfig { Name = "data-agent" })
    .WithServiceProvider(provider)
    .WithToolHarness<DataTools>(options =>
        options.AddSkillsFromStore(SkillQuery.WithTag("domain", "data")));
```

Filter the store when sharing it between harnesses. The unfiltered overload is
suitable only when one harness owns every package in the store.

Package versions are immutable. Use `UpdateAsync` with the expected current
version, then refresh an already-built agent manually:

```csharp
await store.UpdateAsync("data-analysis", versionTwo, expectedVersion: "1");

var refresh = await agent.RefreshCapabilitiesAsync();
if (!refresh.Published)
    Console.WriteLine(refresh.Error);
```

Automatic store and directory change notifications use the same atomic refresh
path. A failed refresh retains the complete prior capability epoch.

## Resources and scripts

Resources are read-only parameterless functions revealed by activation. Use
`DelegateSkillResource`, `EmbeddedSkillResource`,
`ContentStoreSkillResource`, or `FileSkillResource` for non-inline content.

Scripts require a host-provided `ISkillScriptRunner`; the framework does not
ship an in-process Python, JavaScript, or shell interpreter:

```csharp
public sealed class PythonSkillRunner : ISkillScriptRunner
{
    public bool CanRun(SkillScript script) =>
        script.Reference.Runtime == "python";

    public async ValueTask<object?> RunAsync(
        SkillScriptExecutionContext context,
        CancellationToken cancellationToken)
    {
        await using var process = StartIsolatedProcess();
        await context.Arguments.WriteJsonAsync(
            process.StandardInput.BaseStream,
            cancellationToken);
        process.StandardInput.Close();
        // Read the bounded result from the runner-owned process.
        return "completed";
    }

    private static Process StartIsolatedProcess() =>
        throw new NotImplementedException("Host-specific isolation goes here.");
}
```

Register exactly one compatible runner:

```csharp
services.AddSingleton<ISkillScriptRunner, PythonSkillRunner>();
```

The runner receives detached canonical JSON with defaults materialized. Scripts
require permission by default, activation does not execute scripts, and the
framework enforces the declared timeout and output limit.

## Lifecycle and diagnostics

- Skill activation lasts for the current message turn.
- Activation emits started, activated, and failed events.
- Resource reads and script execution emit bounded lifecycle events.
- Instructions normally return in the activation tool result.
- Configure `SkillInstructionDelivery.ToolResultWithSystemReinforcement` only
  when declared reinforcement should also enter subsequent system context.
- Agent capability refresh publishes a new epoch only after the complete
  candidate validates.

Use `RefreshCapabilitiesAsync()`, not a skill-specific reload method, for manual
refresh after out-of-process store updates.
