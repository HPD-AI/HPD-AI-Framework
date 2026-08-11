using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class AuthoritySchemaLedgerSourceGeneratorTests
{
    [Fact]
    public void ConsumerWithoutLedger_IsUnaffected()
    {
        var compilation = CSharpCompilation.Create("consumer", [CSharpSyntaxTree.ParseText("internal sealed class C { }")]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new AuthoritySchemaLedgerSourceGenerator().AsSourceGenerator());

        var result = driver.RunGenerators(compilation).GetRunResult();

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void MissingSchemaRow_FailsBeforeEmission()
    {
        var ledger = LoadLedger();
        var line = ledger.Split('\n').First(row => row.StartsWith("hpd.journal-position.v1|1.0|", StringComparison.Ordinal));

        var result = Run(ledger.Replace(line + "\n", string.Empty, StringComparison.Ordinal));

        Assert.Single(result.Diagnostics, item => item.Id == "HPDA002");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void DuplicateSchemaField_FailsBeforeEmission()
    {
        var ledger = LoadLedger();
        const string marker = "@AxisValueBindings";
        var field = ledger.Split('\n').First(row => row.StartsWith("hpd.session-authority-stamp.v1|1|", StringComparison.Ordinal));

        var result = Run(ledger.Replace(marker, field + "\n" + marker, StringComparison.Ordinal));

        Assert.Single(result.Diagnostics, item => item.Id == "HPDA002");
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult Run(string ledger)
    {
        var compilation = CSharpCompilation.Create("authority", [CSharpSyntaxTree.ParseText("internal sealed class C { }")]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AuthoritySchemaLedgerSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new LedgerText(ledger)],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static string LoadLedger()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(root, "src/HPD-Agent/Authority/Generated/authority-schema-ledger-v1.txt"));
    }

    private sealed class LedgerText(string text) : AdditionalText
    {
        public override string Path => "authority-schema-ledger-v1.txt";
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }
}
