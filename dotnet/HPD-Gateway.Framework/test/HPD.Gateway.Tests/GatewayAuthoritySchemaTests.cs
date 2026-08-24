using FluentAssertions;
using HPD.Base;
using HPD.Gateway.ControlPlane;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayAuthoritySchemaTests
{
    [Fact]
    public void GeneratedCollectionsHaveClosedStableIdentityAndMutationModes()
    {
        var collections = new CollectionDefinition[]
        {
            GatewayAcceptedRevision.Collection.Definition,
            GatewayValidationRecord.Collection.Definition,
            GatewayAdministrativeAuditRecord.Collection.Definition,
            GatewayTargetOwnership.Collection.Definition,
            GatewayDesiredState.Collection.Definition,
            GatewayNodeDeliveryAuthorityState.Collection.Definition,
            GatewayActivationIntent.Collection.Definition,
            GatewayDeliveryOutboxItem.Collection.Definition,
            GatewayNodeActivationOutcome.Collection.Definition,
            GatewayCommandReceipt.Collection.Definition,
            GatewayAdministrativeOperationIntent.Collection.Definition,
            GatewayAdministrativeArtifactObservation.Collection.Definition,
            GatewayAdministrativeExecutionState.Collection.Definition,
            GatewayAdministrativeOperationObservation.Collection.Definition,
            GatewayAdministrativeOperationCompletion.Collection.Definition,
            GatewayPurgeAuthorityState.Collection.Definition,
        };

        collections.Select(static value => value.Id).Should().OnlyHaveUniqueItems();
        collections.Should().OnlyContain(static value => value.SchemaMode == SchemaMode.Strict);
        collections.Should().OnlyContain(static value => value.UnknownFields == UnknownFieldPolicy.Reject);
        collections.SelectMany(static value => value.Fields ?? []).Select(static value => value.Id)
            .Should().OnlyHaveUniqueItems();

        GatewayTargetOwnership.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.AppendOnly);
        GatewayDesiredState.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.Mutable);
        GatewayNodeDeliveryAuthorityState.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.Mutable);
        GatewayDeliveryOutboxItem.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.Mutable);
        GatewayAdministrativeExecutionState.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.Mutable);
        GatewayPurgeAuthorityState.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.Mutable);
        GatewayCommandReceipt.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.AppendOnly);
        GatewayAcceptedRevision.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge);
        GatewayAdministrativeAuditRecord.Collection.Definition.MutationMode.Should().Be(BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge);
    }

    [Fact]
    public void SingletonIdsIgnoreNamespaceOperationAndIdempotencyIdentity()
    {
        const string firstNamespace = "namespace-a";
        const string firstKey = "key-a";
        const string competingNamespaceId = "namespace-b";
        const string competingKey = "key-b";
        RecordId ownership = GatewayAuthorityRecordIds.TargetOwnership("authority", "node-1");
        RecordId competingNamespace = GatewayAuthorityRecordIds.TargetOwnership("authority", "node-1");
        RecordId delivery = GatewayAuthorityRecordIds.NodeDeliveryAuthority("authority", "node-1");
        RecordId desired = GatewayAuthorityRecordIds.DesiredState("authority", "node-1");

        competingNamespace.Should().Be(ownership,
            "a different namespace and idempotency key must still address the target singleton");
        GatewayAuthorityRecordIds.CommandFact("target-provision", firstNamespace, "provision", firstKey)
            .Should().NotBe(GatewayAuthorityRecordIds.CommandFact(
                "target-provision", competingNamespaceId, "provision", competingKey));
        delivery.Should().NotBe(ownership);
        desired.Should().NotBe(ownership);
        GatewayAuthorityRecordIds.TargetOwnership("authority", "node-2").Should().NotBe(ownership);
    }

    [Fact]
    public void CommandFactIdsAreRetryDeterministicAndPurposeSeparated()
    {
        RecordId first = GatewayAuthorityRecordIds.CommandFact("revision", "namespace", "submit", "key", "v1");
        RecordId retry = GatewayAuthorityRecordIds.CommandFact("revision", "namespace", "submit", "key", "v1");
        RecordId receipt = GatewayAuthorityRecordIds.CommandFact("receipt", "namespace", "submit", "key", "v1");

        retry.Should().Be(first);
        receipt.Should().NotBe(first);
        GatewayAuthorityRecordIds.CommandFact("revision", "other", "submit", "other-key", "v1")
            .Should().NotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nidentity")]
    public void IdentityInputsFailClosed(string value)
    {
        Action create = () => GatewayAuthorityRecordIds.TargetOwnership("authority", value);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BinaryRecordValuesAreDefensivelyOwned()
    {
        byte[] source = [1, 2, 3];
        var receipt = new GatewayCommandReceipt
        {
            NamespaceId = "namespace",
            TargetNodeId = "node",
            Operation = "submit",
            IdempotencyKey = "key",
            Fingerprint = BaseBinary.From(source),
            StableResultCode = "accepted",
            StableOperationId = "revision",
        };

        source[0] = 9;
        byte[] copy = receipt.Fingerprint.ToArray();
        copy[1] = 9;
        receipt.Fingerprint.ToArray().Should().Equal(1, 2, 3);
    }
}
