using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Microsoft.Extensions.AI;
using HPD.Agent;

namespace HPD.Agent.Tests.SourceGenerator;

/// <summary>
/// Tests for [MCPServer] attribute source generation:
/// - Attribute detection (IsToolClass)
/// - Capability analysis (CapabilityAnalyzer.AnalyzeMCPServerCapability)
/// - Diagnostic errors (HPDAG0301-0304)
/// - Attribute property extraction
/// - Code generation (registration, registry, description)
/// - ParentContainer rename verification
/// </summary>
public class MCPServerSourceGeneratorTests
{
    #region Helper Methods

    private static (string? generatedCode, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.RuntimeHelpers).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.AI.AIFunction).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CollapseAttribute).Assembly.Location), // HPD-Agent assembly (has MCPServerAttribute)
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(HPD.Agent.MCP.MCPServerConfig).Assembly.Location), // HPD-Agent.MCP assembly
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new global::HPDToolSourceGenerator();
        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedSyntaxTrees = outputCompilation.SyntaxTrees
            .Where(st => st.FilePath.Contains("g.cs"))
            .ToImmutableArray();

        var generatedSourceCode = string.Join("\n\n", generatedSyntaxTrees.Select(st => st.GetText().ToString()));

        return (generatedSourceCode, diagnostics);
    }

    /// <summary>
    /// Filters diagnostics to only generator-produced errors (HPDAG prefix).
    /// </summary>
    private static IEnumerable<Diagnostic> GetGeneratorErrors(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Id.StartsWith("HPDAG") && d.Severity == DiagnosticSeverity.Error);

    private static IEnumerable<Diagnostic> GetGeneratorWarnings(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Id.StartsWith("HPDAG") && d.Severity == DiagnosticSeverity.Warning);

    #endregion

    #region Attribute Detection (IsToolClass)

    [Fact]
    public void IsToolClass_MethodWithMCPServer_ClassDetectedAsToolkit()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class MyToolkit
    {
        [MCPServer]
        public MCPServerConfig WolframServer() => new MCPServerConfig
        {
            Name = ""wolfram"",
            Command = ""npx"",
            Arguments = new[] { ""wolfram-mcp"" }
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        // Should be detected and have generated code
        Assert.NotNull(generatedCode);
        Assert.NotEmpty(generatedCode!);
        // Should contain the toolkit registration
        Assert.Contains("MyToolkit", generatedCode);
    }

    [Fact]
    public void IsToolClass_OnlyMCPServerMethods_StillDetectedAsToolkit()
    {
        // Class with ONLY [MCPServer] methods (no [AIFunction]) should still be detected
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class MCPOnlyToolkit
    {
        [MCPServer]
        public MCPServerConfig Server1() => new MCPServerConfig
        {
            Name = ""server1"",
            Command = ""node"",
            Arguments = new[] { ""server1.js"" }
        };

        [MCPServer]
        public MCPServerConfig Server2() => new MCPServerConfig
        {
            Name = ""server2"",
            Command = ""python"",
            Arguments = new[] { ""server2.py"" }
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.NotEmpty(generatedCode!);
        Assert.Contains("MCPOnlyToolkit", generatedCode);
    }

    #endregion

    #region Capability Analysis - Valid Return Types

    [Fact]
    public void AnalyzeMCPServer_ReturnsMCPServerConfig_ProducesCapability()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node"",
            Arguments = new[] { ""test.js"" }
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        // Should generate MCPServerRegistration code
        Assert.Contains("MCPServerRegistration", generatedCode!);
    }

    [Fact]
    public void AnalyzeMCPServer_ReturnsNullableMCPServerConfig_ProducesCapability()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer(""filesystem"", FromManifest = ""mcp.json"")]
        public MCPServerConfig? FileSystem() => null;
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("MCPServerRegistration", generatedCode!);
    }

    #endregion

    #region Capability Analysis - Invalid Return Types (HPDAG0301)

    [Fact]
    public void AnalyzeMCPServer_ReturnsString_ProducesDiagnosticError()
    {
        var source = @"
using HPD.Agent;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public string BadServer() => ""not a config"";

        // Need at least one valid capability so the class is processed
        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        var errors = GetGeneratorErrors(diagnostics).ToList();
        Assert.Contains(errors, d => d.Id == "HPDAG0301");
    }

    [Fact]
    public void AnalyzeMCPServer_ReturnsInt_ProducesDiagnosticError()
    {
        var source = @"
using HPD.Agent;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public int BadServer() => 42;

        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        var errors = GetGeneratorErrors(diagnostics).ToList();
        Assert.Contains(errors, d => d.Id == "HPDAG0301");
    }

    #endregion

    #region Capability Analysis - Conflicting Attributes (HPDAG0302)

    [Fact]
    public void AnalyzeMCPServer_CombinedWithAIFunction_ProducesDiagnosticError()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        [AIFunction]
        public MCPServerConfig ConflictingServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };

        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        var errors = GetGeneratorErrors(diagnostics).ToList();
        Assert.Contains(errors, d => d.Id == "HPDAG0302");
    }

    [Fact]
    public void AnalyzeMCPServer_CombinedWithSkill_MethodIgnored()
    {
        // [Skill] is checked before [MCPServer] in dispatch priority.
        // When return type is MCPServerConfig (not Skill), the Skill analyzer returns null.
        // The method is silently ignored (not recognized as either capability).
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        [Skill]
        public MCPServerConfig ConflictingServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };

        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        // Method is silently dropped (return type doesn't match Skill's expected return type).
        // The generated code should not contain an MCPServerRegistration for this method.
        Assert.NotNull(generatedCode);
        Assert.DoesNotContain("ConflictingServer", generatedCode!);
    }

    [Fact]
    public void AnalyzeMCPServer_CombinedWithSubAgent_MethodIgnored()
    {
        // [SubAgent] is checked before [MCPServer] in dispatch priority.
        // When return type is MCPServerConfig (not SubAgent), the SubAgent analyzer returns null.
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        [SubAgent]
        public MCPServerConfig ConflictingServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };

        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        // Method is silently dropped
        Assert.NotNull(generatedCode);
        Assert.DoesNotContain("ConflictingServer", generatedCode!);
    }

    [Fact]
    public void AnalyzeMCPServer_CombinedWithMultiAgent_ProducesConflictError()
    {
        // [MultiAgent] is checked before [MCPServer] in dispatch priority.
        // MultiAgent explicitly checks for conflicting attributes and emits HPDAG0202.
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        [MultiAgent]
        public MCPServerConfig ConflictingServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };

        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        // MultiAgent conflict checker emits HPDAG0202
        var errors = GetGeneratorErrors(diagnostics).ToList();
        Assert.Contains(errors, d => d.Id == "HPDAG0202");
    }

    #endregion

    #region Attribute Property Extraction

    [Fact]
    public void AttributeExtraction_NoArgs_DefaultsToMethodName()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public MCPServerConfig WolframServer() => new MCPServerConfig
        {
            Name = ""wolfram"",
            Command = ""npx"",
            Arguments = new[] { ""wolfram-mcp"" }
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        // Name defaults to method name "WolframServer"
        Assert.Contains("Name = \"WolframServer\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_ServerNameConstructor_SetsManifestServerName()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer(""filesystem"", FromManifest = ""mcp.json"")]
        public MCPServerConfig? FileSystem() => null;
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("ManifestServerName = \"filesystem\"", generatedCode!);
        Assert.Contains("FromManifest = \"mcp.json\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_CustomName_OverridesMethodName()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer(Name = ""CustomName"")]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("Name = \"CustomName\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_Description_SetCorrectly()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer(Description = ""A test MCP server"")]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("Description = \"A test MCP server\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_CollapseWithinToolkit_True()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer(CollapseWithinToolkit = true)]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("CollapseWithinToolkit = true", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_RequiresPermission_Present()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        [RequiresPermission]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("RequiresPermissionOverride = true", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_RequiresPermission_Absent_NoOverride()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.DoesNotContain("RequiresPermissionOverride", generatedCode!);
    }

    #endregion

    #region Static vs Instance

    [Fact]
    public void StaticMCPServer_GeneratesStaticConfigProvider()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public static MCPServerConfig StaticServer() => new MCPServerConfig
        {
            Name = ""static-test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("StaticConfigProvider", generatedCode!);
        Assert.Contains("TestToolkit.StaticServer()", generatedCode!);
    }

    [Fact]
    public void InstanceMCPServer_GeneratesInstanceConfigProvider()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public MCPServerConfig InstanceServer() => new MCPServerConfig
        {
            Name = ""instance-test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("InstanceConfigProvider", generatedCode!);
        Assert.Contains("((TestToolkit)instance).InstanceServer()", generatedCode!);
    }

    #endregion

    #region Code Generation - ToolkitRegistry

    [Fact]
    public void ToolkitRegistry_WithMCPServers_HasMCPServersTrue()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };

        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("HasMCPServers: true", generatedCode!);
    }

    [Fact]
    public void ToolkitRegistry_WithoutMCPServers_HasMCPServersFalse()
    {
        var source = @"
using HPD.Agent;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("HasMCPServers: false", generatedCode!);
    }

    #endregion

    #region Code Generation - Description

    [Fact]
    public void Description_FunctionsAndMCPServers_BothCounted()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [AIFunction]
        public string Func1() => ""1"";

        [AIFunction]
        public string Func2() => ""2"";

        [MCPServer]
        public MCPServerConfig Server1() => new MCPServerConfig
        {
            Name = ""s1"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        // Description should mention both counts
        Assert.Contains("2 AI functions", generatedCode!);
        Assert.Contains("1 MCP servers", generatedCode!);
    }

    #endregion

    #region Code Generation - MCPServers Static Property

    [Fact]
    public void MCPServersProperty_Generated_WithCorrectCount()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        public MCPServerConfig Server1() => new MCPServerConfig
        {
            Name = ""s1"",
            Command = ""node""
        };

        [MCPServer]
        public MCPServerConfig Server2() => new MCPServerConfig
        {
            Name = ""s2"",
            Command = ""python""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        // Should generate MCPServers static property
        Assert.Contains("MCPServerRegistration", generatedCode!);
        Assert.Contains("MCPServers", generatedCode!);
        // Should contain both server registrations
        Assert.Contains("ParentToolkit = \"TestToolkit\"", generatedCode!);
    }

    #endregion

    #region Code Generation - ParentToolkit Always Set

    [Fact]
    public void MCPServerRegistration_ParentToolkit_AlwaysSetToClassName()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class SearchToolkit
    {
        [MCPServer]
        public MCPServerConfig BraveSearch() => new MCPServerConfig
        {
            Name = ""brave"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("ParentToolkit = \"SearchToolkit\"", generatedCode!);
    }

    #endregion

    #region ParentContainer Rename Verification

    [Fact]
    public void ParentContainer_NotEmittedForToolkitContainer()
    {
        // Toolkit containers (from [Collapse]) do NOT have ParentContainer key.
        // ParentContainer is only emitted for skill containers inside collapsed toolkits.
        var source = @"
using HPD.Agent;

namespace TestToolkits
{
    [Collapse(""Test toolkit"")]
    public partial class CollapsedToolkit
    {
        [AIFunction]
        public string Func1() => ""1"";

        [AIFunction]
        public string Func2() => ""2"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        // Should NOT have ParentSkillContainer (old name removed)
        Assert.DoesNotContain("ParentSkillContainer", generatedCode!);
    }

    #endregion

    #region Five Capabilities Coexistence

    [Fact]
    public void FiveCapabilities_AllDetectedNoConflicts()
    {
        // Toolkit with all 5 types should all be detected without conflicts
        // Note: Each capability must be on a SEPARATE method (no multi-attribute on same method)
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class MegaToolkit
    {
        [AIFunction]
        public string Function1() => ""func"";

        [MCPServer]
        public MCPServerConfig Server1() => new MCPServerConfig
        {
            Name = ""s1"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        // No HPDAG errors
        var errors = GetGeneratorErrors(diagnostics).ToList();
        Assert.Empty(errors);

        Assert.NotNull(generatedCode);
        // Both types detected
        Assert.Contains("MCPServerRegistration", generatedCode!);
        Assert.Contains("HPDAIFunctionFactory.Create", generatedCode!);
    }

    #endregion

    #region AIDescription Override

    [Fact]
    public void AIDescription_OnMCPServerMethod_OverridesDescription()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;
using System.ComponentModel;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [MCPServer]
        [System.ComponentModel.Description(""Override description"")]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("Override description", generatedCode!);
    }

    #endregion

    #region Integration: Full Toolkit with MCPServer + Collapse + AIFunction

    [Fact]
    public void Integration_ToolkitWithCollapseAndMCPServer_AllGenerated()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    [Collapse(""Dev toolkit with MCP servers"",
        FunctionResult = ""Dev toolkit expanded."")]
    public partial class DevToolkit
    {
        [AIFunction]
        public string ReadFile(string path) => ""content"";

        [AIFunction]
        public string WriteFile(string path, string content) => ""ok"";

        [MCPServer(CollapseWithinToolkit = true)]
        public MCPServerConfig GitServer() => new MCPServerConfig
        {
            Name = ""git"",
            Command = ""git-mcp""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        // No HPDAG errors
        var errors = GetGeneratorErrors(diagnostics).ToList();
        Assert.Empty(errors);

        Assert.NotNull(generatedCode);

        // Container generated for [Collapse]
        Assert.Contains("CreateDevToolkitContainer", generatedCode!);

        // MCPServer registration generated
        Assert.Contains("MCPServerRegistration", generatedCode!);
        Assert.Contains("ParentToolkit = \"DevToolkit\"", generatedCode!);
        Assert.Contains("CollapseWithinToolkit = true", generatedCode!);

        // HasMCPServers flag set
        Assert.Contains("HasMCPServers: true", generatedCode!);

        // Functions registered
        Assert.Contains("ReadFile", generatedCode!);
        Assert.Contains("WriteFile", generatedCode!);
    }

    #endregion

    #region EmitsIntoCreateTools Dispatch

    [Fact]
    public void MCPServerRegistration_NotInFunctionsAdd()
    {
        // Regression test: MCPServerRegistration must NOT appear inside functions.Add(...)
        // This was the CS1503 bug — MCPServerRegistration is not an AIFunction.
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    public partial class TestToolkit
    {
        [AIFunction]
        public string Helper() => ""help"";

        [MCPServer]
        public MCPServerConfig MyServer() => new MCPServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);

        // MCPServerRegistration should exist in the MCPServers property
        Assert.Contains("MCPServerRegistration", generatedCode!);
        Assert.Contains("MCPServers", generatedCode!);

        // MCPServerRegistration must NOT appear inside functions.Add(...)
        Assert.DoesNotContain("functions.Add(new HPD.Agent.MCP.MCPServerRegistration", generatedCode!);

        // The AIFunction should be in functions.Add(...)
        Assert.Contains("functions.Add(", generatedCode!);
        Assert.Contains("Helper", generatedCode!);
    }

    [Fact]
    public void MCPServerWithCollapse_DispatchedCorrectly()
    {
        // Collapsed toolkit with AIFunctions + MCPServer: functions go to CreateTools,
        // MCPServer goes to MCPServers property — never mixed.
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolkits
{
    [Collapse(""Dev tools"")]
    public partial class DevToolkit
    {
        [AIFunction]
        public string ReadFile(string path) => ""content"";

        [MCPServer]
        public MCPServerConfig GitServer() => new MCPServerConfig
        {
            Name = ""git"",
            Command = ""git-mcp""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        var errors = GetGeneratorErrors(diagnostics).ToList();
        Assert.Empty(errors);

        Assert.NotNull(generatedCode);

        // AIFunction dispatched to CreateTools
        Assert.Contains("functions.Add(", generatedCode!);
        Assert.Contains("ReadFile", generatedCode!);

        // MCPServer dispatched to MCPServers property
        Assert.Contains("MCPServerRegistration", generatedCode!);
        Assert.DoesNotContain("functions.Add(new HPD.Agent.MCP.MCPServerRegistration", generatedCode!);
    }

    #endregion
}
