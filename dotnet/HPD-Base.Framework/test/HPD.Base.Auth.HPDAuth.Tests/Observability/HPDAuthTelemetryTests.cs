using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base.Observability;
using HPD.Base.Tests.Observability;

namespace HPD.Base.Auth.HPDAuth.Tests.Observability;

public sealed class HPDAuthTelemetryTests
{
    [Fact]
    public async Task PolicyAndGrantTelemetryDoNotLeakIdentityClaimsRolesOrGrantMarkers()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.HPDAuth);
        using var metrics = new MeterCollector(HPDBaseMeterNames.HPDAuth);
        var services = new ServiceCollection();
        services.AddHPDBaseHPDAuth(options =>
        {
            options.RequireHPDAuthServices = false;
            options.AllowAdminBypass = true;
        });
        services.AddSingleton<IHPDAuthBaseGrantProvider>(new SecretGrantProvider());
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(Principal()));
        var bypass = await evaluator.EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Admin,
            SubjectKind = AccessSubjectKind.Admin,
            SubjectId = "admin-subject-secret",
            CurrentTenantId = "tenant-secret",
            Roles = ["role-secret"]
        }));
        var missingHostServices = new ServiceCollection();
        missingHostServices.AddHPDBaseHPDAuth();
        using var missingHostProvider = missingHostServices.BuildServiceProvider();
        await missingHostProvider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(Principal()));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        bypass.Outcome.Should().Be(PolicyOutcome.Bypassed);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.AuthPolicyEvaluate);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.AuthGrantsResolve);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.AuthHostCheck);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.AuthPolicyEvaluations);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.AuthPolicyDuration);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.AuthGrantProviderCalls);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.AuthGrantsMatched);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.AuthBypasses);

        var forbidden = new[]
        {
            "subject-secret",
            "admin-subject-secret",
            "tenant-secret",
            "role-secret",
            "claim-secret",
            "grant-secret",
            "token-secret",
            "email-secret",
            "display-secret",
            "credential-secret",
            "session-secret",
            "owner-field-secret"
        };
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value => forbidden.Any(marker => value.Contains(marker, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task PolicyEvaluationWorksWithoutConfiguredTelemetryListeners()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseHPDAuth(options => options.RequireHPDAuthServices = false);
        services.AddSingleton<IHPDAuthBaseGrantProvider>(new SecretGrantProvider());
        using var provider = services.BuildServiceProvider();

        var decision = await provider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(Principal()));

        decision.Effect.Should().Be(PolicyEffect.Allow);
    }

    private static PrincipalContext Principal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Authenticated,
        SubjectKind = AccessSubjectKind.User,
        SubjectId = "subject-secret",
        CurrentTenantId = "tenant-secret",
        DisplayName = "display-secret",
        Roles = ["role-secret"],
        Claims = [new ClaimValue { Type = "token", Value = "token-secret" }, new ClaimValue { Type = "email", Value = "email-secret" }],
        SessionId = "session-secret",
        CredentialId = "credential-secret"
    };

    private static PolicyEvaluationRequest Request(PrincipalContext principal) => new()
    {
        Principal = principal,
        Operation = new OperationContext
        {
            Operation = BaseOperationKind.List,
            CollectionId = "items",
            CorrelationId = "corr-secret",
            Now = DateTimeOffset.UnixEpoch
        },
        Collection = new CollectionDefinition
        {
            Id = "items",
            Name = "items",
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose,
            UnknownFields = UnknownFieldPolicy.Preserve
        },
        Resource = new PolicyResource { Kind = PolicyResourceKind.Query }
    };

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

    private sealed class SecretGrantProvider : IHPDAuthBaseGrantProvider
    {
        public ValueTask<IReadOnlyList<AccessGrant>> GetGrantsAsync(
            HPDAuthBaseGrantRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<AccessGrant> grants =
            [
                new AccessGrant
                {
                    Id = "grant-secret",
                    Effect = GrantEffect.Allow,
                    Action = HPDAuthBasePolicyActions.Read,
                    Subject = new AccessSubject
                    {
                        Kind = AccessSubjectKind.User,
                        Id = "subject-secret",
                        TenantId = "tenant-secret"
                    },
                    Scope = new ResourceScope
                    {
                        Kind = ResourceScopeKind.Collection,
                        CollectionId = request.Collection.Id
                    },
                    Condition = new FilterExpression
                    {
                        Kind = FilterNodeKind.Compare,
                        Field = "owner-field-secret",
                        Operator = FilterOperator.Equal,
                        Value = new QueryValue { Kind = QueryValueKind.String, String = "subject-secret" }
                    }
                }
            ];

            return ValueTask.FromResult(grants);
        }
    }

}
