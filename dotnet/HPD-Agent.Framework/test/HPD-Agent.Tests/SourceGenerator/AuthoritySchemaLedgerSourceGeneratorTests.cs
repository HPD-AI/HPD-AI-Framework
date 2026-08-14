using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System.Security.Cryptography;

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

    [Fact]
    public void ReservationV2Ledger_GeneratesExactRowsAndHash()
    {
        var ledger=LoadLedger();
        var result=Run(ledger);
        Assert.Contains("hpd.authority-owner-payload.v1|43|GraphParticipantReservationCommandV2|hpd.authority-payload-graph-participant-reservation-command.v2|GraphParticipantReservationCommandV2|S1",ledger,StringComparison.Ordinal);
        Assert.Contains("hpd.authority-owner-payload.v1|44|GraphParticipantReservationFactV2|hpd.authority-payload-graph-participant-reservation-fact.v2|GraphParticipantReservationFactV2|S1",ledger,StringComparison.Ordinal);
        Assert.Contains("hpd.authority-owner-payload.v1|45|GraphMediaPhysicalReleaseCommand|hpd.authority-payload-graph-media-physical-release-command.v1|GraphMediaPhysicalReleaseOuterV1|S1",ledger,StringComparison.Ordinal);
        Assert.Contains("hpd.authority-owner-payload.v1|46|GraphMediaPhysicalReleaseFact|hpd.authority-payload-graph-media-physical-release-fact.v1|GraphMediaPhysicalReleaseOuterV1|S1",ledger,StringComparison.Ordinal);
        Assert.Equal("c1c4c31b8d72f7ed28c6793c9ce776d0c38d465e26aa2a5dc897463c758e4972",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ledger))).ToLowerInvariant());
        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("hpd.authority-owner-payload.v1|43|GraphParticipantReservationCommandV2|hpd.authority-payload-graph-participant-reservation-command.v2|GraphParticipantReservationCommandV2|S1","hpd.authority-owner-payload.v1|42|GraphParticipantReservationCommandV2|hpd.authority-payload-graph-participant-reservation-command.v2|GraphParticipantReservationCommandV2|S1")]
    [InlineData("hpd.authority-owner-payload.v1|44|GraphParticipantReservationFactV2|hpd.authority-payload-graph-participant-reservation-fact.v2|GraphParticipantReservationFactV2|S1","hpd.authority-owner-payload.v1|43|GraphParticipantReservationFactV2|hpd.authority-payload-graph-participant-reservation-fact.v2|GraphParticipantReservationFactV2|S1")]
    [InlineData("hpd.authority-owner-payload.v1|45|GraphMediaPhysicalReleaseCommand|hpd.authority-payload-graph-media-physical-release-command.v1|GraphMediaPhysicalReleaseOuterV1|S1","hpd.authority-owner-payload.v1|44|GraphMediaPhysicalReleaseCommand|hpd.authority-payload-graph-media-physical-release-command.v1|GraphMediaPhysicalReleaseOuterV1|S1")]
    [InlineData("hpd.authority-owner-payload.v1|46|GraphMediaPhysicalReleaseFact|hpd.authority-payload-graph-media-physical-release-fact.v1|GraphMediaPhysicalReleaseOuterV1|S1","hpd.authority-owner-payload.v1|45|GraphMediaPhysicalReleaseFact|hpd.authority-payload-graph-media-physical-release-fact.v1|GraphMediaPhysicalReleaseOuterV1|S1")]
    public void ReservationV2LedgerMutation_FailsBeforeEmission(string original,string mutated)
    {
        var ledger=LoadLedger();
        Assert.Equal(1,ledger.Split(original,StringSplitOptions.None).Length-1);
        var mutatedLedger=ledger.Replace(original,mutated,StringComparison.Ordinal);
        Assert.NotEqual(ledger,mutatedLedger);
        var result=Run(mutatedLedger);
        Assert.Single(result.Diagnostics,item=>item.Id=="HPDA002");
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
