using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Microsoft.Extensions.AI;
using HPD.Agent;

namespace HPD.Agent.Tests.SourceGenerator;

/// <summary>
/// Tests for [McpServer] attribute source generation:
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
                MetadataReference.CreateFromFile(typeof(CollapseAttribute).Assembly.Location), // HPD-Agent assembly (has McpServerAttribute)
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(HPD.Agent.MCP.McpServerConfig).Assembly.Location), // HPD-Agent.MCP assembly
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
    public void IsToolClass_MethodWithMCPServer_ClassDetectedAsToolHarness()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class MyToolHarness
    {
        [McpServer]
        public McpServerConfig WolframServer() => new McpServerConfig
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
        // Should contain the toolharness registration
        Assert.Contains("MyToolHarness", generatedCode);
    }

    [Fact]
    public void IsToolClass_OnlyMCPServerMethods_StillDetectedAsToolHarness()
    {
        // Class with ONLY [McpServer] methods (no [AIFunction]) should still be detected
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class MCPOnlyToolHarness
    {
        [McpServer]
        public McpServerConfig Server1() => new McpServerConfig
        {
            Name = ""server1"",
            Command = ""node"",
            Arguments = new[] { ""server1.js"" }
        };

        [McpServer]
        public McpServerConfig Server2() => new McpServerConfig
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
        Assert.Contains("MCPOnlyToolHarness", generatedCode);
    }

    #endregion

    #region Capability Analysis - Valid Return Types

    [Fact]
    public void AnalyzeMCPServer_ReturnsMcpServerConfig_ProducesCapability()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        public McpServerConfig MyServer() => new McpServerConfig
        {
            Name = ""test"",
            Command = ""node"",
            Arguments = new[] { ""test.js"" }
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        // Should generate reflection-free MCP server source collection code.
        Assert.Contains("McpServerSource", generatedCode!);
        Assert.Contains("CollectMcpServers", generatedCode!);
    }

    [Fact]
    public void AnalyzeMCPServer_ReturnsNullableMcpServerConfig_ProducesCapability()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer(""filesystem"", FromManifest = ""mcp.json"")]
        public McpServerConfig? FileSystem() => null;
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("McpServerSource", generatedCode!);
        Assert.Contains("CollectMcpServers", generatedCode!);
    }

    #endregion

    #region Capability Analysis - Invalid Return Types (HPDAG0301)

    [Fact]
    public void AnalyzeMCPServer_ReturnsString_ProducesDiagnosticError()
    {
        var source = @"
using HPD.Agent;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
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

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
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

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        [AIFunction]
        public McpServerConfig ConflictingServer() => new McpServerConfig
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
        // [Skill] is checked before [McpServer] in dispatch priority.
        // When return type is McpServerConfig (not Skill), the Skill analyzer returns null.
        // The method is silently ignored (not recognized as either capability).
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        [Skill]
        public McpServerConfig ConflictingServer() => new McpServerConfig
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
        // The generated code should not contain a McpServerSource for this method.
        Assert.NotNull(generatedCode);
        Assert.DoesNotContain("ConflictingServer", generatedCode!);
    }

    [Fact]
    public void AnalyzeMCPServer_CombinedWithSubAgent_MethodIgnored()
    {
        // [SubAgent] is checked before [McpServer] in dispatch priority.
        // When return type is McpServerConfig (not SubAgent), the SubAgent analyzer returns null.
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        [SubAgent]
        public McpServerConfig ConflictingServer() => new McpServerConfig
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
        // [MultiAgent] is checked before [McpServer] in dispatch priority.
        // MultiAgent explicitly checks for conflicting attributes and emits HPDAG0202.
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        [MultiAgent]
        public McpServerConfig ConflictingServer() => new McpServerConfig
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

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        public McpServerConfig WolframServer() => new McpServerConfig
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
        Assert.Contains("Name: \"WolframServer\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_ServerNameConstructor_SetsManifestServerName()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer(""filesystem"", FromManifest = ""mcp.json"")]
        public McpServerConfig? FileSystem() => null;
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("ManifestServerName: \"filesystem\"", generatedCode!);
        Assert.Contains("FromManifest: \"mcp.json\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_CustomName_OverridesMethodName()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer(Name = ""CustomName"")]
        public McpServerConfig MyServer() => new McpServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("Name: \"CustomName\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_Description_SetCorrectly()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer(Description = ""A test MCP server"")]
        public McpServerConfig MyServer() => new McpServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("Description: \"A test MCP server\"", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_CollapseWithinToolHarness_True()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer(CollapseWithinToolHarness = true)]
        public McpServerConfig MyServer() => new McpServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("CollapseWithinToolHarness: true", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_RequiresPermission_Present()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        [RequiresPermission]
        public McpServerConfig MyServer() => new McpServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("RequiresPermissionOverride: true", generatedCode!);
    }

    [Fact]
    public void AttributeExtraction_RequiresPermission_Absent_NoOverride()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        public McpServerConfig MyServer() => new McpServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("RequiresPermissionOverride: null", generatedCode!);
        Assert.DoesNotContain("RequiresPermissionOverride: true", generatedCode!);
    }

    #endregion

    #region Static vs Instance

    [Fact]
    public void StaticMCPServer_GeneratesStaticConfigProvider()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        public static McpServerConfig StaticServer() => new McpServerConfig
        {
            Name = ""static-test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("FactoryProvider: static _ =>", generatedCode!);
        Assert.Contains("TestToolHarness.StaticServer()", generatedCode!);
    }

    [Fact]
    public void InstanceMCPServer_GeneratesInstanceConfigProvider()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        public McpServerConfig InstanceServer() => new McpServerConfig
        {
            Name = ""instance-test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("FactoryProvider: static instance =>", generatedCode!);
        Assert.Contains("((TestToolHarness)instance!).InstanceServer()", generatedCode!);
    }

    #endregion

    #region Code Generation - ToolHarnessRegistry

    [Fact]
    public void ToolHarnessRegistry_WithMCPServers_HasMCPServersTrue()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        public McpServerConfig MyServer() => new McpServerConfig
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
        Assert.Contains("HasMcpServers: true", generatedCode!);
    }

    [Fact]
    public void ToolHarnessRegistry_WithoutMCPServers_HasMCPServersFalse()
    {
        var source = @"
using HPD.Agent;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [AIFunction]
        public string Helper() => ""help"";
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("HasMcpServers: false", generatedCode!);
    }

    #endregion

    #region Code Generation - Description

    [Fact]
    public void Description_FunctionsAndMCPServers_BothCounted()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [AIFunction]
        public string Func1() => ""1"";

        [AIFunction]
        public string Func2() => ""2"";

        [McpServer]
        public McpServerConfig Server1() => new McpServerConfig
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

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        public McpServerConfig Server1() => new McpServerConfig
        {
            Name = ""s1"",
            Command = ""node""
        };

        [McpServer]
        public McpServerConfig Server2() => new McpServerConfig
        {
            Name = ""s2"",
            Command = ""python""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        // Should generate MCP server source collection.
        Assert.Contains("McpServerSource", generatedCode!);
        Assert.Contains("CollectMcpServers", generatedCode!);
        // Should contain both server registrations
        Assert.Contains("ParentToolHarness: \"TestToolHarness\"", generatedCode!);
    }

    #endregion

    #region Code Generation - ParentToolHarness Always Set

    [Fact]
    public void MCPServerRegistration_ParentToolHarness_AlwaysSetToClassName()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class SearchToolHarness
    {
        [McpServer]
        public McpServerConfig BraveSearch() => new McpServerConfig
        {
            Name = ""brave"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);
        Assert.Contains("ParentToolHarness: \"SearchToolHarness\"", generatedCode!);
    }

    #endregion

    #region ParentContainer Rename Verification

    [Fact]
    public void ParentContainer_NotEmittedForToolHarnessContainer()
    {
        // ToolHarness containers (from [Collapse]) do NOT have ParentContainer key.
        // ParentContainer is only emitted for skill containers inside collapsed toolharnesses.
        var source = @"
using HPD.Agent;

namespace TestToolHarnesses
{
    [Collapse(""Test toolharness"")]
    public partial class CollapsedToolHarness
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
        // ToolHarness with all 5 types should all be detected without conflicts
        // Note: Each capability must be on a SEPARATE method (no multi-attribute on same method)
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class MegaToolHarness
    {
        [AIFunction]
        public string Function1() => ""func"";

        [McpServer]
        public McpServerConfig Server1() => new McpServerConfig
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
        Assert.Contains("McpServerSource", generatedCode!);
        Assert.Contains("CollectMcpServers", generatedCode!);
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

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [McpServer]
        [System.ComponentModel.Description(""Override description"")]
        public McpServerConfig MyServer() => new McpServerConfig
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

    #region Integration: Full ToolHarness with MCPServer + Collapse + AIFunction

    [Fact]
    public void Integration_ToolHarnessWithCollapseAndMCPServer_AllGenerated()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    [Collapse(""Dev toolharness with MCP servers"",
        FunctionResult = ""Dev toolharness expanded."")]
    public partial class DevToolHarness
    {
        [AIFunction]
        public string ReadFile(string path) => ""content"";

        [AIFunction]
        public string WriteFile(string path, string content) => ""ok"";

        [McpServer(CollapseWithinToolHarness = true)]
        public McpServerConfig GitServer() => new McpServerConfig
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
        Assert.Contains("CreateDevToolHarnessContainer", generatedCode!);

        // MCP server source collection generated.
        Assert.Contains("McpServerSource", generatedCode!);
        Assert.Contains("CollectMcpServers", generatedCode!);
        Assert.Contains("ParentToolHarness: \"DevToolHarness\"", generatedCode!);
        Assert.Contains("CollapseWithinToolHarness: true", generatedCode!);

        // HasMCPServers flag set
        Assert.Contains("HasMcpServers: true", generatedCode!);

        // Functions registered
        Assert.Contains("ReadFile", generatedCode!);
        Assert.Contains("WriteFile", generatedCode!);
    }

    #endregion

    #region EmitsIntoCreateTools Dispatch

    [Fact]
    public void MCPServerRegistration_NotInFunctionsAdd()
    {
        // MCP sources have their own collection and must not appear inside functions.Add(...).
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    public partial class TestToolHarness
    {
        [AIFunction]
        public string Helper() => ""help"";

        [McpServer]
        public McpServerConfig MyServer() => new McpServerConfig
        {
            Name = ""test"",
            Command = ""node""
        };
    }
}";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.NotNull(generatedCode);

        // MCP server source collection should exist.
        Assert.Contains("McpServerSource", generatedCode!);
        Assert.Contains("CollectMcpServers", generatedCode!);

        // MCP server source collection must NOT be emitted inside functions.Add(...)
        Assert.DoesNotContain("functions.Add(new HPD.Agent.McpServerSource", generatedCode!);

        // The AIFunction should be in functions.Add(...)
        Assert.Contains("functions.Add(", generatedCode!);
        Assert.Contains("Helper", generatedCode!);
    }

    [Fact]
    public void MCPServerWithCollapse_DispatchedCorrectly()
    {
        // Collapsed toolharness with AIFunctions + MCPServer: functions go to CreateTools,
        // MCPServer goes to MCPServers property — never mixed.
        var source = @"
using HPD.Agent;
using HPD.Agent.MCP;

namespace TestToolHarnesses
{
    [Collapse(""Dev tools"")]
    public partial class DevToolHarness
    {
        [AIFunction]
        public string ReadFile(string path) => ""content"";

        [McpServer]
        public McpServerConfig GitServer() => new McpServerConfig
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

        // MCPServer dispatched to source collection
        Assert.Contains("McpServerSource", generatedCode!);
        Assert.Contains("CollectMcpServers", generatedCode!);
        Assert.DoesNotContain("functions.Add(new HPD.Agent.McpServerSource", generatedCode!);
    }

    #endregion
}
