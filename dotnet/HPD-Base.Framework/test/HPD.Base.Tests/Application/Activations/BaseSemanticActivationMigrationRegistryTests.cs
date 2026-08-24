using System.Collections.Immutable;
using FluentAssertions;

namespace HPD.Base.Tests;

public sealed class BaseSemanticActivationMigrationRegistryTests
{
    [Fact]
    public void MatchesInstalledChain_rejects_hostile_chain_variants()
    {
        BaseSemanticActivationDefinitionKey v1 = Key(1, 1);
        BaseSemanticActivationDefinitionKey v2 = Key(2, 2);
        BaseSemanticActivationDefinitionKey v3 = Key(3, 3);
        BaseSemanticActivationMigrationDefinition first = Migration("first", 1, v1, v2);
        BaseSemanticActivationMigrationDefinition second = Migration("second", 1, v2, v3);
        var registry = new BaseSemanticActivationMigrationRegistry([first, second]);
        BaseSemanticActivationDefinitionIdentity target = Identity(v3);
        BaseSemanticActivationDefinitionMigrationAuthority a = Authority(first, 1);
        BaseSemanticActivationDefinitionMigrationAuthority b = Authority(second, 2);

        registry.MatchesInstalledChain(v1, target, [a, b]).Should().BeTrue();
        registry.MatchesInstalledChain(v1, target, [a]).Should().BeFalse("omission cannot shorten installed authority");
        registry.MatchesInstalledChain(v1, target, [a, b, b]).Should().BeFalse("additional edges cannot extend installed authority");
        registry.MatchesInstalledChain(v1, target, [b, a]).Should().BeFalse("provider order cannot replace graph order");

        BaseSemanticActivationMigrationDefinition fabricated = Migration("alternate", 9, v1, v3);
        registry.MatchesInstalledChain(v1, target, [Authority(fabricated, 1)]).Should().BeFalse(
            "a self-consistent provider-authored alternate path is not installed graph authority");
        registry.MatchesInstalledChain(v1, target, [Authority(first with { Id = "substituted" }, 1), b]).Should().BeFalse();
    }

    private static BaseSemanticActivationDefinitionKey Key(int version, byte marker) => new()
    {
        Id = "payments.semantic", Version = version,
        Checksum = Enumerable.Repeat(marker, 32).Select(static value => (byte)value).ToImmutableArray(),
    };

    private static BaseSemanticActivationDefinitionIdentity Identity(BaseSemanticActivationDefinitionKey value) => new()
    { Id = value.Id, Version = value.Version, Checksum = value.Checksum, OwnerGeneration = 1,
      OwningModuleId = "payments", RetirementOperation = new() { OperationId = "complete", OperationVersion = 1, OperationChecksum = new string('0', 64) } };

    private static BaseSemanticActivationMigrationDefinition Migration(string id, int version,
        BaseSemanticActivationDefinitionKey from, BaseSemanticActivationDefinitionKey to) =>
        BaseSemanticActivationMigrationContract.Seal(new() { Id = id, Version = version, From = from, To = to, Checksum = [] });

    private static BaseSemanticActivationDefinitionMigrationAuthority Authority(
        BaseSemanticActivationMigrationDefinition definition, long generation)
    {
        var value = new BaseSemanticActivationDefinitionMigrationAuthority
        {
            MigrationId = definition.Id, MigrationVersion = definition.Version,
            From = definition.From, To = definition.To, ExpectedLiveCount = 0,
            ExpectedRetiredCount = 1, ExpectedAbsenceCount = 0,
            OrderedNegativeAuthorityChecksum = Enumerable.Repeat((byte)7, 32).ToImmutableArray(),
            PublicationGeneration = generation,
            ReceiptChecksum = Enumerable.Repeat((byte)8, 32).ToImmutableArray(), Checksum = [],
        };
        return value with { Checksum = BaseSemanticActivationMigrationAuthorityContract.Checksum(value) };
    }
}
