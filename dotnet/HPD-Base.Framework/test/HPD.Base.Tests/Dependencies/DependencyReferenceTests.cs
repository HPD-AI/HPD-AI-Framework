using System.Text;
using System.Text.Json;
using System.Reflection;
using FluentAssertions;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests;

public sealed class DependencyReferenceTests
{
    [Fact]
    public void ReferencesAreDeterministicOpaqueAndKeyScoped()
    {
        const string tenant = "tenant-secret";
        const string record = "record-secret";
        var first = Factory(1).Create(
            BaseDependencyIds.Record,
            new BaseDependencyParameter("record", record),
            new BaseDependencyParameter("collection", "items"),
            new BaseDependencyParameter("tenant", tenant));
        var repeated = Factory(1).Create(
            BaseDependencyIds.Record,
            new BaseDependencyParameter("tenant", tenant),
            new BaseDependencyParameter("collection", "items"),
            new BaseDependencyParameter("record", record));
        var otherKey = Factory(2).Create(
            BaseDependencyIds.Record,
            new BaseDependencyParameter("tenant", tenant),
            new BaseDependencyParameter("collection", "items"),
            new BaseDependencyParameter("record", record));

        first.Should().Be(repeated);
        first.Should().NotBe(otherKey);
        first.Value.Should().StartWith("d1.");

        var decoded = Convert.FromBase64String(
            first.Value[3..].Replace('-', '+').Replace('_', '/')
                .PadRight((first.Value.Length - 3 + 3) / 4 * 4, '='));
        var decodedText = Encoding.UTF8.GetString(decoded);
        decodedText.Should().NotContain(tenant);
        decodedText.Should().NotContain(record);
        decoded.Should().HaveCount(32);
    }

    [Fact]
    public async Task RecordMutationProducesCollectionAndRecordInvalidations()
    {
        var mapper = Provider(3).GetRequiredService<IBaseDependencyInvalidationMapper>();
        var mutation = Mutation();

        var invalidation = await mapper.MapAsync(mutation);

        invalidation.EventId.Should().Be(mutation.EventId);
        invalidation.Reason.Should().Be(BaseDependencyInvalidationReasons.RecordMutation);
        invalidation.References.Select(reference => reference.TemplateId)
            .Should().Equal(BaseDependencyIds.Collection, BaseDependencyIds.Record);
        JsonSerializer.Serialize(
                invalidation,
                HPDBaseDependenciesJsonSerializerContext.Default.BaseDependencyInvalidation)
            .Should().NotContain("tenant-secret")
            .And.NotContain("record-secret")
            .And.NotContain("items");
    }

    [Fact]
    public async Task CustomRulesAreProtectedDeduplicatedAndBounded()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseMutationDependencyRule, DuplicateRule>();
        services.AddHPDBaseDependencies(
            options =>
            {
                options.ProtectionKey = Key(4);
                options.MaxReferencesPerInvalidation = 3;
            },
            new BaseDependencyTemplate
            {
                Id = "cloud.project",
                Kind = BaseDependencyKind.Named,
                ParameterNames = ["project"]
            });
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IBaseDependencyInvalidationMapper>()
            .MapAsync(Mutation());

        result.References.Should().HaveCount(3);
        result.References.Count(reference => reference.TemplateId == "cloud.project").Should().Be(1);
        result.References.Should().OnlyContain(reference => !reference.Value.Contains("project-secret", StringComparison.Ordinal));
    }

    [Fact]
    public void RegistrationRejectsMissingProtectionAndInvalidTemplates()
    {
        var missingKey = () => new ServiceCollection().AddHPDBaseDependencies(_ => { });
        var rawTemplate = () => new ServiceCollection().AddHPDBaseDependencies(
            options => options.ProtectionKey = Key(5),
            new BaseDependencyTemplate
            {
                Id = "cloud.projects.{projectId}",
                Kind = BaseDependencyKind.Named,
                ParameterNames = ["projectId"]
            });

        missingKey.Should().Throw<ArgumentException>();
        rawTemplate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ReferenceLimitFailsClosedInsteadOfDroppingInvalidations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseMutationDependencyRule, TooManyRule>();
        services.AddHPDBaseDependencies(
            options =>
            {
                options.ProtectionKey = Key(7);
                options.MaxReferencesPerInvalidation = 2;
            },
            new BaseDependencyTemplate
            {
                Id = "cloud.project",
                Kind = BaseDependencyKind.Named,
                ParameterNames = ["project"]
            });
        using var provider = services.BuildServiceProvider();

        var map = async () => await provider.GetRequiredService<IBaseDependencyInvalidationMapper>()
            .MapAsync(Mutation());

        await map.Should().ThrowAsync<BaseDependencyInvalidationException>();
    }

    [Fact]
    public async Task PublicDescriptorContainsTemplatesButNeverResolvedValues()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseDependencies(
            options => options.ProtectionKey = Key(6),
            new BaseDependencyTemplate
            {
                Id = "cloud.project",
                Kind = BaseDependencyKind.Named,
                ParameterNames = ["projectId"]
            },
            new BaseDependencyTemplate
            {
                Id = "cloud.internal",
                Kind = BaseDependencyKind.AuthContext,
                ParameterNames = ["subject"],
                Visibility = BaseDependencyVisibility.Internal
            });
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IBaseDescriptorRegistry>();
        var snapshot = await registry.RebuildAsync();

        var module = snapshot.Manifest.Modules!.Single(item => item.Id == BaseDependencyModuleIds.Module);
        var metadata = module.PublicConfig!["templates"].GetRawText();

        metadata.Should().Contain("cloud.project");
        metadata.Should().Contain("projectId");
        metadata.Should().NotContain("cloud.internal");
        metadata.Should().NotContain("project-secret");
        metadata.Should().NotContain("subject");
    }

    [Fact]
    public void PublicTemplateMetadataRejectsUnboundedOrMalformedValues()
    {
        BaseDependencyTemplate[] invalid =
        [
            Template(description: new string('x', 513)),
            Template(description: "safe\nresolved-value"),
            Template(parameterName: "project{id}"),
            Template(parameterName: "project/id"),
            Template(kind: (BaseDependencyKind)999),
            Template(visibility: (BaseDependencyVisibility)999)
        ];

        foreach (var template in invalid)
        {
            var register = () => new ServiceCollection().AddHPDBaseDependencies(
                options => options.ProtectionKey = Key(8),
                template);
            register.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void DependencyRuntimeIsIntentionallyLogAndTelemetryFree()
    {
        var assembly = typeof(IBaseDependencyReferenceFactory).Assembly;
        var forbiddenFieldTypes = assembly.GetTypes()
            .Where(static type => type.Namespace?.StartsWith(
                "HPD.Base.Dependencies", StringComparison.Ordinal) is true)
            .SelectMany(static type => type.GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(static field => field.FieldType.FullName)
            .Where(static name => name is not null
                && (name.Contains("ILogger", StringComparison.Ordinal)
                    || name.Contains("ActivitySource", StringComparison.Ordinal)
                    || name.Contains("System.Diagnostics.Metrics.Meter", StringComparison.Ordinal)))
            .ToArray();
        forbiddenFieldTypes.Should().BeEmpty();
    }

    private static IBaseDependencyReferenceFactory Factory(byte seed) =>
        Provider(seed).GetRequiredService<IBaseDependencyReferenceFactory>();

    private static ServiceProvider Provider(byte seed)
    {
        var services = new ServiceCollection();
        services.AddHPDBaseDependencies(options => options.ProtectionKey = Key(seed));
        return services.BuildServiceProvider();
    }

    private static byte[] Key(byte seed) => Enumerable.Repeat(seed, 32).ToArray();

    private static BaseDependencyTemplate Template(
        string? description = "safe",
        string parameterName = "projectId",
        BaseDependencyKind kind = BaseDependencyKind.Named,
        BaseDependencyVisibility visibility = BaseDependencyVisibility.Public) => new()
        {
            Id = "cloud.project",
            Kind = kind,
            ParameterNames = [parameterName],
            Visibility = visibility,
            Description = description
        };

    private static BaseRecordMutationEvent Mutation() => new()
    {
        EventId = "event-one",
        Type = BaseEventTypes.RecordCreated,
        SchemaVersion = BaseEventSchemaVersions.V1,
        TenantId = "tenant-secret",
        Visibility = VisibilityLevel.Public,
        Resource = new EventResource
        {
            Kind = EventResourceKind.Record,
            CollectionId = "items",
            RecordId = RecordId.Create("record-secret")
        },
        Operation = BaseOperationKind.Create
    };

    private sealed class DuplicateRule : IBaseMutationDependencyRule
    {
        public ValueTask<IReadOnlyList<BaseDependencyInput>> ResolveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default)
        {
            BaseDependencyInput input = new()
            {
                TemplateId = "cloud.project",
                Parameters = [new BaseDependencyParameter("project", "project-secret")]
            };
            return ValueTask.FromResult<IReadOnlyList<BaseDependencyInput>>([input, input]);
        }
    }

    private sealed class TooManyRule : IBaseMutationDependencyRule
    {
        public ValueTask<IReadOnlyList<BaseDependencyInput>> ResolveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<BaseDependencyInput>>(
            [
                new BaseDependencyInput
                {
                    TemplateId = "cloud.project",
                    Parameters = [new BaseDependencyParameter("project", "project-secret")]
                }
            ]);
    }
}
