using HPD.Agent.SourceGenerator.Capabilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Agent.Tests;

public class HarnessSymbolDiscoveryTests
{
    private const string Attributes = """
        using System;
        public class AIFunctionAttribute : Attribute { }
        public class AIFunctionAttribute<T> : Attribute { }
        public class SkillAttribute : Attribute { }
        public class SkillAttribute<T> : Attribute { }
        public class SubAgentAttribute : Attribute { }
        public class SubAgentAttribute<T> : Attribute { }
        namespace HPD.Agent
        {
            public class MultiAgentAttribute : Attribute { }
            public class MultiAgentAttribute<T> : Attribute { }
            public class McpServerAttribute : Attribute { }
            public class McpServerAttribute<T> : Attribute { }
            public class OpenApiAttribute : Attribute { }
        }
        """;

    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    private static CSharpCompilation Compile(string name, string source,
        params MetadataReference[] additional) => CSharpCompilation.Create(name,
        [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
        References.Concat(additional), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static MetadataReference Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    [Theory]
    [InlineData("AIFunction", CapabilityType.Function)]
    [InlineData("AIFunction<string>", CapabilityType.Function)]
    [InlineData("Skill", CapabilityType.Skill)]
    [InlineData("Skill<string>", CapabilityType.Skill)]
    [InlineData("SubAgent", CapabilityType.SubAgent)]
    [InlineData("SubAgent<string>", CapabilityType.SubAgent)]
    [InlineData("HPD.Agent.MultiAgent", CapabilityType.MultiAgent)]
    [InlineData("HPD.Agent.MultiAgent<string>", CapabilityType.MultiAgent)]
    [InlineData("HPD.Agent.McpServer", CapabilityType.MCPServer)]
    [InlineData("HPD.Agent.McpServer<string>", CapabilityType.MCPServer)]
    [InlineData("HPD.Agent.OpenApi", CapabilityType.OpenApi)]
    public void ClassifiesEveryKindFromCompiledBase(string attribute, object expected)
    {
        var producer = Emit(Compile("Producer", Attributes + $$"""
            public class Base { [{{attribute}}] public void Tool() { } }
            """));
        var consumer = Compile("Consumer", "public class Derived : Base { }", producer);
        _ = Emit(consumer);
        var method = Assert.Single(HarnessSymbolDiscovery.GetCapabilityMethods(
            consumer.GetTypeByMetadataName("Derived")!, consumer));
        Assert.Empty(method.DeclaringSyntaxReferences);
        Assert.Equal((CapabilityType)expected, HarnessSymbolDiscovery.Classify(
            HarnessSymbolDiscovery.GetCapabilityAttribute(method, consumer)!, consumer));
        Assert.Equal(attribute.Contains('<'), HarnessSymbolDiscovery.GetMetadataType(method, consumer) is not null);
    }

    [Fact]
    public void OverrideInheritsContractOnceAcrossCompiledGenericBase()
    {
        var producer = Emit(Compile("GenericProducer", Attributes + """
            public class Base<T>
            {
                [AIFunction<string>] public virtual T Echo(T input) => input;
            }
            """));
        var consumer = Compile("GenericConsumer", """
            public class Derived : Base<string>
            {
                public override string Echo(string input) => input;
            }
            """, producer);
        _ = Emit(consumer);
        var method = Assert.Single(HarnessSymbolDiscovery.GetCapabilityMethods(
            consumer.GetTypeByMetadataName("Derived")!, consumer));
        Assert.Equal("Derived", method.ContainingType.Name);
        Assert.Equal(SpecialType.System_String, method.OverriddenMethod!.Parameters[0].Type.SpecialType);
        Assert.Equal(SpecialType.System_String,
            HarnessSymbolDiscovery.GetMetadataType(method, consumer)!.SpecialType);
    }

    [Fact]
    public void ExplicitNewMemberReplacesInheritedCapability()
    {
        var compilation = Compile("Hidden", Attributes + """
            public class Base { [AIFunction] public void Run() { } }
            public class Derived : Base { [AIFunction] public new void Run() { } }
            """);
        _ = Emit(compilation);
        var method = Assert.Single(HarnessSymbolDiscovery.GetCapabilityMethods(
            compilation.GetTypeByMetadataName("Derived")!, compilation));
        Assert.Equal("Derived", method.ContainingType.Name);
    }

    [Fact]
    public void UnmarkedHiddenMembersRemainAvailableForCollisionDiagnostics()
    {
        var compilation = Compile("UnmarkedHidden", Attributes + """
            public class Base { [AIFunction] public void Run(int value) { } }
            public class Derived : Base { [AIFunction] public void Run(string value) { } }
            """);
        _ = Emit(compilation);
        Assert.Equal(2, HarnessSymbolDiscovery.GetCapabilityMethods(
            compilation.GetTypeByMetadataName("Derived")!, compilation).Count());
    }

    [Fact]
    public void UnrelatedSameNamedAttributeIsNotACapability()
    {
        var compilation = Compile("Unrelated", Attributes + """
            namespace Other { public class AIFunctionAttribute : System.Attribute { } }
            public class Harness { [Other.AIFunction] public void Tool() { } }
            """);
        _ = Emit(compilation);
        Assert.Empty(HarnessSymbolDiscovery.GetCapabilityMethods(
            compilation.GetTypeByMetadataName("Harness")!, compilation));
    }

    [Fact]
    public void AliasedAttributeAndPartialTypeAreResolvedBySymbol()
    {
        var compilation = Compile("Aliased", """
            using Workflow = HPD.Agent.MultiAgentAttribute<string>;
            """ + Attributes + """
            public partial class Harness { [Workflow] public void Run() { } }
            public partial class Harness { }
            """);
        _ = Emit(compilation);
        var method = Assert.Single(HarnessSymbolDiscovery.GetCapabilityMethods(
            compilation.GetTypeByMetadataName("Harness")!, compilation));
        Assert.Equal(SpecialType.System_String,
            HarnessSymbolDiscovery.GetMetadataType(method, compilation)!.SpecialType);
    }

    [Fact]
    public void MetadataPropertiesFollowCompiledInheritanceAndHiding()
    {
        var producer = Emit(Compile("MetadataProducer", """
            public class BaseMetadata
            {
                public bool Enabled { get; set; }
                public string Name { get; set; } = "base";
            }
            """));
        var consumer = Compile("MetadataConsumer", """
            public class DerivedMetadata : BaseMetadata
            {
                public new int Name { get; set; }
                public bool Local { get; set; }
                public static bool Static { get; set; }
                public bool WriteOnly { set { } }
            }
            """, producer);
        _ = Emit(consumer);
        var properties = HarnessSymbolDiscovery.GetMetadataProperties(
            consumer.GetTypeByMetadataName("DerivedMetadata")!).ToDictionary(property => property.Name);
        Assert.Equal(3, properties.Count);
        Assert.Contains("Enabled", properties.Keys);
        Assert.Equal(SpecialType.System_Int32, properties["Name"].Type.SpecialType);
    }

    [Fact]
    public void InaccessiblePropertyAllowsAccessibleBaseProperty()
    {
        var compilation = Compile("MetadataHiding", """
            public class BaseMetadata { public bool Enabled { get; set; } }
            public class DerivedMetadata : BaseMetadata { private new bool Enabled { get; set; } }
            public static class Consumer { public static bool Read(DerivedMetadata value) => value.Enabled; }
            """);
        _ = Emit(compilation);
        var property = Assert.Single(HarnessSymbolDiscovery.GetMetadataProperties(
            compilation.GetTypeByMetadataName("DerivedMetadata")!));
        Assert.Equal("BaseMetadata", property.ContainingType.Name);
    }
}
