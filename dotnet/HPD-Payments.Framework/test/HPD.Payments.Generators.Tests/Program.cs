using System.Collections.Immutable;
using System.Text;
using HPD.Payments.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

var tests = new (string Name, Action Run)[]
{
    ("all permitted projections", AllPermittedProjections),
    ("two clean generations byte stable", CleanGenerationIsStable),
    ("generated baseline exact", GeneratedBaselineIsExact),
    ("incremental rerun cached", IncrementalRerunIsCached),
    ("malformed declarations diagnosed", MalformedDeclarationsAreDiagnosed),
    ("semantic declaration mutation diff", DeclarationMutationProducesReviewedDiff),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void AllPermittedProjections()
{
    const string declarations = """
serializer|AuthorityJsonContext|HPD.Payments.Contracts.Agreement.AgreementCommand
table|AuthorityTable|Agreement
visitor|ResultVisitor|Accepted
factory|PersistenceFactory|Agreement
dependency|AgreementDependency|ScopedIdentity
compatibility|WireRevision|rev-1
oracle|AgreementOracle|AGR-001
connector|SimulatorConnector|authorize
testvector|AgreementReplay|duplicate-request
""";
    var result = Generate(declarations);
    Equal(0, result.Diagnostics.Length, "valid declarations diagnostics");
    foreach (var kind in new[] { "serializer", "table", "visitor", "factory", "dependency", "compatibility", "oracle", "connector", "testvector" })
        Contains(result.Source, kind + "|", "projection " + kind);
}

static void CleanGenerationIsStable()
{
    const string declarations = "table|B|2\ntable|A|1\n";
    var first = Generate(declarations);
    var second = Generate(declarations);
    Equal(first.Source, second.Source, "clean generation bytes");
    True(first.Source.IndexOf("table|A|1", StringComparison.Ordinal) < first.Source.IndexOf("table|B|2", StringComparison.Ordinal), "ordinal output");
}

static void GeneratedBaselineIsExact()
{
    var root = AppContext.BaseDirectory;
    var declarations = File.ReadAllText(Path.Combine(root, "Baselines", "reviewed.hpdpayments"));
    var expected = File.ReadAllText(Path.Combine(root, "Baselines", "HPD.Payments.ReviewedDeclarations.g.cs"));
    Equal(expected.Replace("\r\n", "\n", StringComparison.Ordinal), Generate(declarations).Source, "reviewed generated baseline");
}

static void IncrementalRerunIsCached()
{
    var compilation = CreateCompilation();
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        generators: [new ReviewedDeclarationGenerator().AsSourceGenerator()],
        additionalTexts: [new MemoryAdditionalText("reviewed.hpdpayments", "table|A|1\n")],
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    driver = driver.RunGenerators(compilation);
    driver = driver.RunGenerators(compilation);
    var reasons = driver.GetRunResult().Results.Single().TrackedSteps.Values
        .SelectMany(static steps => steps).SelectMany(static step => step.Outputs)
        .Select(static output => output.Reason).ToArray();
    True(reasons.Contains(IncrementalStepRunReason.Cached) || reasons.Contains(IncrementalStepRunReason.Unchanged), "cached incremental reason");
}

static void MalformedDeclarationsAreDiagnosed()
{
    var result = Generate("table|missing\nauthority|Bad|value\ntable|A|1\ntable|A|2\n");
    Equal("HPDPG001,HPDPG002,HPDPG003", string.Join(',', result.Diagnostics.Select(static diagnostic => diagnostic.Id).Order()), "diagnostic IDs");
}

static void DeclarationMutationProducesReviewedDiff()
{
    var before = Generate("compatibility|WireRevision|rev-1\n");
    var after = Generate("compatibility|WireRevision|rev-2\n");
    True(before.Source != after.Source, "mutation changes output");
    Contains(before.Source, "rev-1", "before reviewed value");
    Contains(after.Source, "rev-2", "after reviewed value");
}

static Generation Generate(string declarations)
{
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        generators: [new ReviewedDeclarationGenerator().AsSourceGenerator()],
        additionalTexts: [new MemoryAdditionalText("reviewed.hpdpayments", declarations)]);
    driver = driver.RunGenerators(CreateCompilation());
    var result = driver.GetRunResult().Results.Single();
    return new Generation(result.GeneratedSources.Single().SourceText.ToString(), result.Diagnostics);
}

static CSharpCompilation CreateCompilation() => CSharpCompilation.Create(
    "GeneratorFixture", [CSharpSyntaxTree.ParseText("internal sealed class Fixture { }")],
    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

static void Equal<T>(T expected, T actual, string name) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
}

static void True(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

static void Contains(string value, string expected, string name)
{
    if (!value.Contains(expected, StringComparison.Ordinal)) throw new InvalidOperationException($"{name}: missing {expected}");
}

file sealed record Generation(string Source, ImmutableArray<Diagnostic> Diagnostics);

file sealed class MemoryAdditionalText(string path, string content) : AdditionalText
{
    public override string Path { get; } = path;
    public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content, Encoding.UTF8);
}
