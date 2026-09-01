using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class FirstPartyDurableEventInventoryTests
{
    [Fact]
    public void CheckedInInventory_MatchesEveryFirstPartyDurableEventMarker()
    {
        var sourceRoot = GetSourceRoot();
        var actual = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .SelectMany(ReadDurableEventNames)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = File.ReadAllLines(Path.Combine(GetTestProjectRoot(), "FirstPartyDurableEventInventory.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        actual.Should().Equal(expected,
            "the checked-in durable-event inventory is an intentional compatibility decision; " +
            "additions and removals must update the inventory in the same change");
    }

    [Fact]
    public void ExecuteCommandOutputChunk_IsExplicitlyAbsentFromTheDurableInventory()
    {
        typeof(ExecuteCommandOutputChunkEvent)
            .IsDefined(typeof(HPD.Agent.Serialization.DurableEventAttribute), inherit: false)
            .Should().BeFalse("command output chunks are live-only and durable output is content-addressed");

        File.ReadAllLines(Path.Combine(GetTestProjectRoot(), "FirstPartyDurableEventInventory.txt"))
            .Should().NotContain(line => line.EndsWith(".ExecuteCommandOutputChunkEvent", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ReadDurableEventNames(string path)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetCompilationUnitRoot();
        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!declaration.AttributeLists.SelectMany(list => list.Attributes).Any(IsDurableEventAttribute))
                continue;

            var names = declaration.Ancestors().OfType<TypeDeclarationSyntax>()
                .Reverse()
                .Select(type => type.Identifier.ValueText)
                .Append(declaration.Identifier.ValueText);
            var namespaceName = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(item => item.Name.ToString());

            yield return string.Join('.', namespaceName.Concat(names));
        }
    }

    private static bool IsDurableEventAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        return name is "DurableEvent" or "DurableEventAttribute"
            || name.EndsWith(".DurableEvent", StringComparison.Ordinal)
            || name.EndsWith(".DurableEventAttribute", StringComparison.Ordinal);
    }

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string GetSourceRoot() => Path.GetFullPath(Path.Combine(GetTestProjectRoot(), "..", "..", "src"));

    private static string GetTestProjectRoot()
    {
        var metadata = typeof(FirstPartyDurableEventInventoryTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(item => item.Key == "HpdTestProjectDirectory");
        return metadata.Value!;
    }
}
