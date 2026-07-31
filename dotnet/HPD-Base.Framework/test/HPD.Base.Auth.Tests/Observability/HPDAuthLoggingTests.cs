using HPD.Base.Tests.Observability;
using HPD.Base;

namespace HPD.Base.Auth.Tests.Observability;

public sealed class HPDAuthLoggingTests
{
    [Fact]
    public async Task DiagnosticEvaluationEmitsExactBoundedConfigurationWarnings()
    {
        using var collector = new LogCollector();
        var services = Services(collector);
        await using var provider = services.BuildServiceProvider();

        var diagnostics = await provider.GetRequiredService<IEnumerable<IBaseDiagnosticContributor>>()
            .Single(contributor => contributor is HPDAuthBaseDiagnosticContributor)
            .GetDiagnosticsAsync();

        diagnostics.Should().HaveCount(2);
        AssertContract(collector.RecordsFor(6000).Should().ContainSingle().Subject, 6000);
        AssertContract(collector.RecordsFor(6001).Should().ContainSingle().Subject, 6001);
        AssertSafe(collector, "UserManager<ApplicationUser>", "IAuditLogger", "secret-service");
    }

    [Fact]
    public async Task GrantProviderFailureHasOneGrantAggregateOwner()
    {
        using var collector = new LogCollector();
        var services = Services(collector, options => options.RequireHPDAuthServices = false);
        services.AddSingleton<IHPDAuthBaseGrantProvider>(new ThrowingGrantProvider());
        await using var provider = services.BuildServiceProvider();

        var action = async () => await provider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(Principal()));

        await action.Should().ThrowAsync<InvalidOperationException>();
        AssertContract(collector.Records.Should().ContainSingle().Subject, 6002);
        AssertSafe(collector, ThrowingGrantProvider.Secret);
    }

    [Fact]
    public async Task DenialAndBypassEmitExactClosedDebugEvents()
    {
        using var collector = new LogCollector();
        var services = Services(collector, options =>
        {
            options.RequireHPDAuthServices = false;
            options.AllowAdminBypass = true;
        });
        await using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var denied = await evaluator.EvaluateAsync(Request(Principal()));
        var bypassed = await evaluator.EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Admin,
            SubjectKind = AccessSubjectKind.Admin,
            SubjectId = "private-admin-id"
        }));

        denied.Effect.Should().Be(PolicyEffect.Deny);
        bypassed.Outcome.Should().Be(PolicyOutcome.Bypassed);
        AssertContract(collector.RecordsFor(6003).Should().ContainSingle().Subject, 6003);
        AssertContract(collector.RecordsFor(6004).Should().ContainSingle().Subject, 6004);
        AssertSafe(collector, "private-subject-id", "private-admin-id");
    }

    [Fact]
    public async Task AllowedGrantEvaluationEmitsNoLogs()
    {
        using var collector = new LogCollector();
        var services = Services(collector, options =>
        {
            options.RequireHPDAuthServices = false;
            options.StaticGrants =
            [
                new AccessGrant
                {
                    Id = "private-grant-id",
                    Effect = GrantEffect.Allow,
                    Action = HPDAuthBasePolicyActions.Read,
                    Subject = new AccessSubject { Kind = AccessSubjectKind.Authenticated },
                    Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime }
                }
            ];
        });
        await using var provider = services.BuildServiceProvider();

        var decision = await provider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(Principal()));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        collector.Records.Should().BeEmpty();
    }

    private static ServiceCollection Services(
        LogCollector collector,
        Action<HPDBaseHPDAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(collector));
        services.AddHPDBaseHPDAuth(configure);
        return services;
    }

    private static void AssertContract(CapturedLogRecord record, int eventId)
    {
        var contract = HPDBaseLogEventRegistry.Active.Single(candidate =>
            candidate.Owner == "HPD.Base.Auth.HPDAuth" && candidate.Id == eventId);
        record.EventId.Id.Should().Be(contract.Id);
        record.EventId.Name.Should().Be(contract.Name);
        record.Level.Should().Be(contract.Level);
        record.OriginalFormat.Should().Be(contract.Template);
        record.State.Where(property => property.Key != "{OriginalFormat}")
            .Select(property => property.Key)
            .Should().Equal(contract.Properties);
    }

    private static void AssertSafe(LogCollector collector, params string[] markers)
    {
        LogSafetyInspector.AssertSafe(collector.Records, markers);
        LogSafetyInspector.AssertNoExceptions(collector.Records);
        LogSafetyInspector.AssertNoScopes(collector.Records);
    }

    private static PrincipalContext Principal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Authenticated,
        SubjectKind = AccessSubjectKind.User,
        SubjectId = "private-subject-id",
        CurrentTenantId = "private-tenant-id",
        Roles = ["private-role"],
        Claims = [new ClaimValue { Type = "private-claim-type", Value = "private-claim-value" }]
    };

    private static PolicyEvaluationRequest Request(PrincipalContext principal) => new()
    {
        Principal = principal,
        Operation = new OperationContext
        {
            Operation = BaseOperationKind.List,
            CollectionId = "private-collection-id",
            Now = DateTimeOffset.UnixEpoch
        },
        Collection = new CollectionDefinition
        {
            Id = "private-collection-id",
            Name = "private-collection-name",
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose,
            UnknownFields = UnknownFieldPolicy.Preserve
        },
        Resource = new PolicyResource { Kind = PolicyResourceKind.Query }
    };

    private sealed class ThrowingGrantProvider : IHPDAuthBaseGrantProvider
    {
        public const string Secret = "private-provider-exception-message";

        public ValueTask<IReadOnlyList<AccessGrant>> GetGrantsAsync(
            HPDAuthBaseGrantRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Secret);
    }
}
