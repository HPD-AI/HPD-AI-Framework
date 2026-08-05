namespace HPD.Base.Auth.Tests.Policy;

public sealed class HPDAuthBasePolicyEvaluatorTests
{
    [Fact]
    public async Task AnonymousFailsClosedByDefault()
    {
        using var provider = Services().BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(PrincipalAuthenticationState.Anonymous));

        decision.Effect.Should().Be(PolicyEffect.Deny);
        decision.Outcome.Should().Be(PolicyOutcome.Unauthenticated);
        decision.ReasonCode.Should().Be("hpd.auth.base.unauthenticated");
    }

    [Fact]
    public async Task MissingRequiredHPDAuthServicesFailClosedBeforeRules()
    {
        using var provider = ServicesWithoutDetectedHost(options =>
        {
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    ReadRoles = ["Reader"],
                    TenantFieldId = "tenantId"
                }
            ];
        }).BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1",
            CurrentTenantId = "tenant-1",
            Roles = ["Reader"]
        }));

        decision.Effect.Should().Be(PolicyEffect.Deny);
        decision.ReasonCode.Should().Be("hpd.auth.base.missingAuthServices");
    }

    [Fact]
    public async Task ClaimOnlyModeCanOperateWithoutDetectedHPDAuthServices()
    {
        using var provider = ServicesWithoutDetectedHost(options =>
        {
            options.RequireHPDAuthServices = false;
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    ReadRoles = ["Reader"],
                    TenantFieldId = "tenantId"
                }
            ];
        }).BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1",
            CurrentTenantId = "tenant-1",
            Roles = ["Reader"]
        }));

        decision.Effect.Should().Be(PolicyEffect.Allow);
    }

    [Fact]
    public async Task AdminBypassAllowsAndAuditsBypass()
    {
        using var provider = Services().BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Admin,
            SubjectKind = AccessSubjectKind.Admin,
            SubjectId = "admin-1",
            Subjects = [new AccessSubject { Kind = AccessSubjectKind.Admin, Id = "admin-1" }]
        };

        var decision = await evaluator.EvaluateAsync(Request(principal));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Outcome.Should().Be(PolicyOutcome.Bypassed);
        decision.Audit!.AdminBypass.Should().BeTrue();
    }

    [Fact]
    public async Task CollectionRuleAllowsRoleAndAddsTenantFilterAndMasks()
    {
        using var provider = Services(options =>
        {
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    ReadRoles = ["Reader"],
                    TenantFieldId = "tenantId",
                    ReadExcludeFields = ["secret"]
                }
            ];
        }).BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1",
            CurrentTenantId = "tenant-1",
            Roles = ["Reader"]
        };

        var decision = await evaluator.EvaluateAsync(Request(principal));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Outcome.Should().Be(PolicyOutcome.AllowedWithConstraints);
        decision.Constraints!.RecordFilter!.Field.Should().Be("tenantId");
        decision.Constraints.RecordFilter.Value!.String.Should().Be("tenant-1");
        decision.Constraints.ReadMask!.Mode.Should().Be(FieldMaskMode.Exclude);
        decision.Constraints.ReadMask.Exclude.Should().Contain("secret");
    }

    [Fact]
    public async Task DenyGrantWinsOverAllowGrant()
    {
        var subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "user-1" };
        using var provider = Services(options =>
        {
            options.StaticGrants =
            [
                Grant("allow", GrantEffect.Allow, subject),
                Grant("deny", GrantEffect.Deny, subject)
            ];
        }).BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1"
        }));

        decision.Effect.Should().Be(PolicyEffect.Deny);
        decision.ReasonCode.Should().Be("hpd.auth.base.grantDenied");
        decision.Audit!.MatchedGrantIds.Should().ContainSingle("deny");
    }

    [Fact]
    public async Task ServiceBypassIsOptIn()
    {
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "svc-1",
            Subjects =
            [
                new AccessSubject
                {
                    Kind = AccessSubjectKind.ServicePrincipal,
                    Id = "svc-1",
                    Source = HPDAuthBaseSources.Auth
                }
            ]
        };
        using var deniedProvider = Services().BuildServiceProvider();
        using var allowedProvider = Services(options => options.AllowServiceBypass = true).BuildServiceProvider();

        var denied = await deniedProvider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(principal));
        var allowed = await allowedProvider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(principal));

        denied.Effect.Should().Be(PolicyEffect.Deny);
        allowed.Effect.Should().Be(PolicyEffect.Allow);
        allowed.Outcome.Should().Be(PolicyOutcome.Bypassed);
        allowed.Audit!.ServiceBypass.Should().BeTrue();
    }

    [Fact]
    public async Task GrantProviderCanAllowReadWithRecordFilter()
    {
        var services = Services();
        services.AddSingleton<IHPDAuthBaseGrantProvider>(new AllowingGrantProvider());
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1"
        }));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Outcome.Should().Be(PolicyOutcome.AllowedWithConstraints);
        decision.Audit!.MatchedGrantIds.Should().ContainSingle("provider-allow");
        decision.Constraints!.RecordFilter!.Field.Should().Be("ownerId");
    }

    [Fact]
    public async Task WriteGrantCanAddExplicitWriteCondition()
    {
        var subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "user-1" };
        using var provider = Services(options =>
        {
            options.StaticGrants =
            [
                Grant("write", GrantEffect.Allow, subject, HPDAuthBasePolicyActions.Create) with
                {
                    WriteCondition = OwnerFilter("user-1")
                }
            ];
        }).BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectKind = AccessSubjectKind.User,
                SubjectId = "user-1"
            },
            BaseOperationKind.Create));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Outcome.Should().Be(PolicyOutcome.AllowedWithConstraints);
        decision.Constraints!.WriteCheck!.Field.Should().Be("ownerId");
        decision.Constraints.RecordFilter.Should().BeNull();
    }

    [Fact]
    public async Task WriteGrantDoesNotReuseReadConditionAsWriteCondition()
    {
        var subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "user-1" };
        using var provider = Services(options =>
        {
            options.StaticGrants =
            [
                Grant("write", GrantEffect.Allow, subject, HPDAuthBasePolicyActions.Create) with
                {
                    Condition = OwnerFilter("user-1")
                }
            ];
        }).BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectKind = AccessSubjectKind.User,
                SubjectId = "user-1"
            },
            BaseOperationKind.Create));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Outcome.Should().Be(PolicyOutcome.Allowed);
        decision.Constraints.Should().BeNull();
    }

    [Fact]
    public async Task GrantRequestExposesNormalizedWriteContext()
    {
        var grantProvider = new CapturingGrantProvider();
        var services = Services();
        services.AddSingleton<IHPDAuthBaseGrantProvider>(grantProvider);
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1",
            CurrentTenantId = "tenant-1"
        };

        await evaluator.EvaluateAsync(Request(principal, BaseOperationKind.Patch) with
        {
            Resource = new PolicyResource
            {
                Kind = PolicyResourceKind.UpdatePayload,
                RecordId = "record-1",
                ProposedPayload = Payload("ownerId", "user-1"),
                ExistingRecord = Record("record-1", Payload("ownerId", "user-1"))
            }
        });

        grantProvider.Request.Should().NotBeNull();
        grantProvider.Request!.Action.Should().Be(BaseOperationKind.Patch);
        grantProvider.Request.CollectionId.Should().Be("items");
        grantProvider.Request.TargetRecordId.Should().Be("record-1");
        grantProvider.Request.ProposedPayload.Should().NotBeNull();
        grantProvider.Request.ExistingRecord.Should().NotBeNull();
        grantProvider.Request.SubjectId.Should().Be("user-1");
        grantProvider.Request.TenantId.Should().Be("tenant-1");
        grantProvider.Request.SubjectFingerprint.Should().HaveLength(64);
        grantProvider.Request.SubjectFingerprint.Should().NotContain("user-1");
        grantProvider.Request.SubjectFingerprint.All(Uri.IsHexDigit).Should().BeTrue();
    }

    [Fact]
    public async Task WriteRuleAddsWriteMaskForPatch()
    {
        using var provider = Services(options =>
        {
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    WriteRoles = ["Editor"],
                    WriteIncludeFields = ["title", "body"]
                }
            ];
        }).BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();

        var decision = await evaluator.EvaluateAsync(Request(
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectKind = AccessSubjectKind.User,
                SubjectId = "user-1",
                Roles = ["Editor"]
            },
            BaseOperationKind.Patch));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Outcome.Should().Be(PolicyOutcome.AllowedWithConstraints);
        decision.Constraints!.WriteMask!.Mode.Should().Be(FieldMaskMode.IncludeOnly);
        decision.Constraints.WriteMask.Include.Should().Contain(["title", "body"]);
    }

    [Fact]
    public async Task HPDAuthThenInnerDeniesWhenInnerDenies()
    {
        var services = Services(options =>
        {
            options.PolicyCompositionMode = HPDAuthBasePolicyCompositionMode.HPDAuthThenInner;
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    ReadRoles = ["Reader"]
                }
            ];
        });
        services.AddSingleton<IHPDAuthBaseInnerPolicyEvaluator>(new FixedInnerPolicyEvaluator(new PolicyDecision
        {
            Effect = PolicyEffect.Deny,
            Outcome = PolicyOutcome.Denied,
            ReasonCode = "inner.denied"
        }));
        using var provider = services.BuildServiceProvider();

        var decision = await provider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1",
            Roles = ["Reader"]
        }));

        decision.Effect.Should().Be(PolicyEffect.Deny);
        decision.ReasonCode.Should().Be("inner.denied");
    }

    [Fact]
    public async Task HPDAuthThenInnerMergesAllowConstraints()
    {
        var services = Services(options =>
        {
            options.PolicyCompositionMode = HPDAuthBasePolicyCompositionMode.HPDAuthThenInner;
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    ReadRoles = ["Reader"],
                    TenantFieldId = "tenantId",
                    ReadIncludeFields = ["title", "ownerId"]
                }
            ];
        });
        services.AddSingleton<IHPDAuthBaseInnerPolicyEvaluator>(new FixedInnerPolicyEvaluator(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.AllowedWithConstraints,
            Constraints = new PolicyConstraints
            {
                RecordFilter = new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "ownerId",
                    Operator = FilterOperator.Equal,
                    Value = new QueryValue { Kind = QueryValueKind.String, String = "user-1" }
                },
                ReadMask = new FieldMask
                {
                    Mode = FieldMaskMode.IncludeOnly,
                    Include = ["title"]
                }
            }
        }));
        using var provider = services.BuildServiceProvider();

        var decision = await provider.GetRequiredService<IPolicyEvaluator>().EvaluateAsync(Request(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "user-1",
            CurrentTenantId = "tenant-1",
            Roles = ["Reader"]
        }));

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Constraints!.RecordFilter!.Kind.Should().Be(FilterNodeKind.And);
        decision.Constraints.ReadMask!.Mode.Should().Be(FieldMaskMode.IncludeOnly);
        decision.Constraints.ReadMask.Include.Should().ContainSingle("title");
    }

    private static ServiceCollection Services(Action<HPDBaseHPDAuthOptions>? configure = null)
    {
        var services = ServicesWithoutDetectedHost(configure);
        services.AddSingleton<IHPDAuthBaseHostIntegrationStatus>(new DetectedHostIntegrationStatus());
        return services;
    }

    private static ServiceCollection ServicesWithoutDetectedHost(Action<HPDBaseHPDAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseHPDAuth(configure);
        return services;
    }

    private static PolicyEvaluationRequest Request(PrincipalAuthenticationState state) =>
        Request(new PrincipalContext
        {
            AuthenticationState = state,
            SubjectKind = state == PrincipalAuthenticationState.Anonymous ? AccessSubjectKind.Anonymous : AccessSubjectKind.User
        });

    private static PolicyEvaluationRequest Request(PrincipalContext principal, BaseOperationKind operation = BaseOperationKind.List) => new()
    {
        Principal = principal,
        Operation = new OperationContext
        {
            Operation = operation,
            CollectionId = "items",
            Now = DateTimeOffset.UnixEpoch
        },
        Collection = Collection(),
        Resource = new PolicyResource { Kind = PolicyResourceKind.Query }
    };

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private static AccessGrant Grant(
        string id,
        GrantEffect effect,
        AccessSubject subject,
        string action = HPDAuthBasePolicyActions.Read) => new()
    {
        Id = id,
        Effect = effect,
        Action = action,
        Subject = subject,
        Scope = new ResourceScope
        {
            Kind = ResourceScopeKind.Collection,
            CollectionId = "items"
        }
    };

    private static FilterExpression OwnerFilter(string ownerId) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = "ownerId",
        Operator = FilterOperator.Equal,
        Value = new QueryValue
        {
            Kind = QueryValueKind.String,
            String = ownerId
        }
    };

    private static RecordPayload Payload(string field, string value) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = new Dictionary<string, JsonElement>
        {
            [field] = JsonSerializer.SerializeToElement(value)
        }
    };

    private static RecordEnvelope Record(string id, RecordPayload payload) => new()
    {
        CollectionId = "items",
        Id = new RecordId(id),
        Payload = payload,
        Metadata = new RecordMetadata
        {
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Revision = new RevisionToken("1")
        }
    };

    private sealed class AllowingGrantProvider : IHPDAuthBaseGrantProvider
    {
        public ValueTask<IReadOnlyList<AccessGrant>> GetGrantsAsync(
            HPDAuthBaseGrantRequest request,
            CancellationToken cancellationToken = default)
        {
            var grants = new[]
            {
                new AccessGrant
                {
                    Id = "provider-allow",
                    Effect = GrantEffect.Allow,
                    Action = HPDAuthBasePolicyActions.Read,
                    Subject = new AccessSubject
                    {
                        Kind = AccessSubjectKind.User,
                        Id = request.Principal.SubjectId
                    },
                    Scope = new ResourceScope
                    {
                        Kind = ResourceScopeKind.Collection,
                        CollectionId = request.Collection.Id
                    },
                    Condition = new FilterExpression
                    {
                        Kind = FilterNodeKind.Compare,
                        Field = "ownerId",
                        Operator = FilterOperator.Equal,
                        Value = new QueryValue
                        {
                            Kind = QueryValueKind.String,
                            String = request.Principal.SubjectId
                        }
                    }
                }
            };

            return ValueTask.FromResult<IReadOnlyList<AccessGrant>>(grants);
        }
    }

    private sealed class DetectedHostIntegrationStatus : IHPDAuthBaseHostIntegrationStatus
    {
        public bool HPDAuthServicesDetected => true;

        public string Source => "test";

        public string[] MissingRequiredServiceNames => [];
    }

    private sealed class CapturingGrantProvider : IHPDAuthBaseGrantProvider
    {
        public HPDAuthBaseGrantRequest? Request { get; private set; }

        public ValueTask<IReadOnlyList<AccessGrant>> GetGrantsAsync(
            HPDAuthBaseGrantRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return ValueTask.FromResult<IReadOnlyList<AccessGrant>>([]);
        }
    }

    private sealed class FixedInnerPolicyEvaluator : IHPDAuthBaseInnerPolicyEvaluator
    {
        private readonly PolicyDecision _decision;

        public FixedInnerPolicyEvaluator(PolicyDecision decision)
        {
            _decision = decision;
        }

        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_decision);
        }
    }
}
