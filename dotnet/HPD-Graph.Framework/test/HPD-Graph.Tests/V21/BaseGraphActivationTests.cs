using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class BaseGraphActivationTests
{
    [Fact]
    public void Registration_binds_the_exact_graph_version_and_codecs()
    {
        GraphConfig graph = Graph("graph-one", "1.0.0", "first");

        BaseGraphActivationDefinition registration =
            BaseGraphActivationRegistration.Create(graph, 1, Grants(), Limits(), []);
        BaseGraphActivationDefinition changed =
            BaseGraphActivationRegistration.Create(graph with { Description = "changed" }, 1, Grants(), Limits(), []);

        registration.Registration.Definition.Id.Should().Be("hpd.graph.execute.graph-one");
        registration.Registration.Definition.ExecutionClass.Should().Be(BaseActivationExecutionClass.AtLeastOnceWorker);
        registration.Registration.Definition.Handler.Should().NotBeNull();
        registration.Registration.Identity.Input.Type.Should().Be(typeof(BaseGraphActivationInput));
        registration.Registration.Identity.Result.Type.Should().Be(typeof(BaseGraphActivationResult));
        registration.Registration.Definition.Checksum.Should().NotEqual(changed.Registration.Definition.Checksum);
    }

    [Fact]
    public void Registration_checksum_is_independent_of_dictionary_insertion_order()
    {
        GraphConfig left = Graph("graph-order", "2.0.0", "ordered") with
        {
            Metadata = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" },
        };
        GraphConfig right = left with
        {
            Metadata = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
        };

        BaseGraphActivationDefinition first =
            BaseGraphActivationRegistration.Create(left, 2, Grants(), Limits(), []);
        BaseGraphActivationDefinition second =
            BaseGraphActivationRegistration.Create(right, 2, Grants(), Limits(), []);

        first.Registration.Definition.Checksum.Should().Equal(second.Registration.Definition.Checksum);
    }

    [Fact]
    public void Installed_builder_owns_the_graph_activation_registration()
    {
        BaseGraphActivationDefinition definition = BaseGraphActivationRegistration.Create(
            Graph("graph-installed", "3.0.0", "installed"), 3, Grants(), Limits(), []);
        var services = new ServiceCollection();

        services.AddHPDBase(builder => builder
            .UseStore(HPDBaseStoreProvider.InMemory)
            .AddGraphActivation(definition));

        using ServiceProvider provider = services.BuildServiceProvider();
        HPDBaseApplication application = provider.GetRequiredService<HPDBaseApplication>();
        BaseInstalledActivationRegistration installed = application.Activations.Resolve(
            definition.Registration.Definition.Id,
            definition.Registration.Definition.Version);
        installed.Definition.Checksum.Should().Equal(definition.Registration.Definition.Checksum);
    }

    private static GraphConfig Graph(string id, string version, string description) => new()
    {
        GraphId = id,
        GraphVersion = version,
        Name = id,
        Description = description,
        Nodes = new Dictionary<string, NodeConfig>(StringComparer.Ordinal),
        Edges = [],
    };

    private static BaseActivationGrantSet Grants() => new()
    {
        Enqueue = "graph.enqueue", Observe = "graph.observe", Claim = "graph.claim",
        Execute = "graph.execute", Renew = "graph.renew", Complete = "graph.complete",
        Fail = "graph.fail", Cancel = "graph.cancel", Inspect = "graph.inspect",
        Replay = "graph.replay", Migrate = "graph.migrate", Reconcile = "graph.reconcile",
        Retry = "graph.retry", Dispose = "graph.dispose", Remove = "graph.remove", Repair = "graph.repair",
    };

    private static BaseActivationLimits Limits() => new()
    {
        MaximumInputBytes = 1_048_576,
        MaximumResultBytes = 65_536,
        MaximumAttempts = 3,
        MaximumRenewalsPerAttempt = 128,
        MaximumChildrenPerAttempt = 128,
        MaximumLineageDepth = 32,
        LeaseDuration = TimeSpan.FromMinutes(1),
        HandlerTimeout = TimeSpan.FromMinutes(30),
        Provider = new BaseActivationExecutionLimits
        {
            MaximumCandidates = 64, MaximumInputBytes = 1_048_576, MaximumResultBytes = 65_536,
            MaximumEvidenceBytes = 1_048_576, MaximumTransientBytes = 4_194_304,
            MaximumReadIntervals = 64, MaximumIndexOperations = 512,
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
        },
        AtomicCreation = new BaseAtomicMutationExecutionLimits
        {
            MaximumItems = 256, MaximumQueryNodes = 2_048, MaximumQueryDepth = 64,
            MaximumLiteralValues = 2_048, MaximumSelectedRecords = 256, MaximumProducedMutations = 256,
            MaximumQueryExecutions = 1, MaximumPreviousStateRequirements = 256,
            MaximumSelectedBytes = 1_048_576, MaximumEvidenceBytes = 1_048_576,
            MaximumTransientBytes = 4_194_304, MaximumReadIntervals = 256, MaximumSubjectValidations = 256,
            MaximumAuthorityReads = 512, MaximumRequestBytes = 1_048_576, MaximumResultBytes = 1_048_576,
            MaximumReceiptBytes = 1_048_576, MaximumWrittenBytes = 4_194_304, MaximumFactBytes = 4_194_304,
            MaximumJournalBytes = 4_194_304, MaximumGenerationBytes = 1_048_576,
            MaximumRelationChecks = 256, MaximumUniqueConstraintChecks = 256,
            MaximumGenerationReads = 256, MaximumGenerationComparisons = 256, MaximumGenerationIncrements = 256,
            MaximumGuardNodes = 2_048, MaximumGuardDepth = 64, MaximumStatements = 512,
            MaximumBranches = 64, MaximumExpressionNodes = 2_048,
            MaximumRecordCaptures = 256, MaximumRelationTargetCaptures = 256,
            MaximumRetirementProjections = 256, MaximumRetirementBarrierReads = 256,
            MaximumRetirementAcknowledgementReads = 256, MaximumRetirementPublications = 256,
            MaximumRetirementEvidenceBytes = 1_048_576, MaximumRetirementPublicationBytes = 1_048_576,
            Deadlines = new BaseAtomicMutationDeadlines
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
                CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
            },
        },
    };
}
