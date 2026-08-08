using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using HPD.Gateway.Admin;
using HPD.Gateway;
using HPD.Gateway.Management;
using HPD.Gateway.Hosting;
using HPD.Gateway.HPDAuth;
using HPD.Auth.ControlPlane;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayAdminContractTests
{
    [Fact]
    public void Client_semantic_ledger_correlates_one_to_one_with_every_endpoint()
    {
        GatewayAdminClientSemanticLedger.V1.Should().HaveCount(23);
        GatewayAdminClientSemanticLedger.V1.Select(static item => item.Operation)
            .Should().OnlyHaveUniqueItems().And.BeEquivalentTo(
                GatewayAdminEndpointLedger.V1.Select(static item => item.Operation));

        foreach (GatewayAdminEndpointDescriptor endpoint in GatewayAdminEndpointLedger.V1)
        {
            GatewayAdminClientOperationSemantics semantics = GatewayAdminClientSemanticLedger.For(endpoint.Operation);
            semantics.Idempotency.Should().Be(endpoint.Mutation
                ? GatewayAdminClientIdempotency.Required
                : GatewayAdminClientIdempotency.Forbidden);
            semantics.RequestBodyPresence.Should().Be(semantics.RequestType is null
                ? GatewayAdminClientRequestBodyPresence.None
                : endpoint.Operation is "activate" or "rollback"
                    ? GatewayAdminClientRequestBodyPresence.Optional
                    : GatewayAdminClientRequestBodyPresence.Required);
            semantics.DesiredPrecondition.Should().Be(endpoint.Operation is
                "submit-and-activate" or "activate" or "rollback" or "import-and-activate"
                    ? GatewayAdminClientDesiredPrecondition.CreateOrReplace
                    : GatewayAdminClientDesiredPrecondition.Forbidden);
            semantics.SuccessMeaning.Should().Be(semantics.SuccessStatus switch
            {
                200 => GatewayAdminClientSuccessMeaning.CompletedRead,
                201 => GatewayAdminClientSuccessMeaning.Created,
                202 => GatewayAdminClientSuccessMeaning.AcceptedNotActive,
                _ => throw new InvalidOperationException(),
            });
            semantics.DocumentedErrors.Should().Equal(
                GatewayAdminOpenApiMetadata.ErrorStatuses(endpoint.Operation));
            semantics.DocumentedErrors.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        }

        GatewayAdminClientOperationSemantics submit = GatewayAdminClientSemanticLedger.For("submit");
        (submit with { Operation = "renamed-without-reclassification" }).DocumentedErrors
            .Should().Equal(submit.DocumentedErrors);
    }

    [Fact]
    public void Pagination_schema_and_description_follow_each_semantic_bound_independently()
    {
        GatewayAdminClientPaginationSpecification baseline =
            GatewayAdminClientPaginationSpecification.OpaqueCursorV1;
        GatewayAdminClientOperationSemantics paged = GatewayAdminClientSemanticLedger.For("revisions");
        GatewayAdminClientPaginationSpecification[] variants =
        [
            baseline with { DefaultMaximum = 65 },
            baseline with { MinimumMaximum = 2 },
            baseline with { MaximumMaximum = 257 },
        ];

        foreach (GatewayAdminClientPaginationSpecification specification in variants)
        {
            var schema = GatewayAdminOpenApiDocumentTransformer.PaginationMaximumSchema(specification);
            schema.Minimum.Should().Be(specification.MinimumMaximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            schema.Maximum.Should().Be(specification.MaximumMaximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            schema.Default!.GetValue<int>().Should().Be(specification.DefaultMaximum);
            string description = GatewayAdminOpenApiDocumentTransformer.PaginationDescription(specification);
            description.Should().Contain(specification.MinimumMaximum!.Value.ToString())
                .And.Contain(specification.DefaultMaximum!.Value.ToString())
                .And.Contain(specification.MaximumMaximum!.Value.ToString());

            var omitted = new DefaultHttpContext();
            GatewayAdminEndpointRouteBuilderExtensions.TryPage(omitted, paged with { Pagination = specification },
                out int defaulted, out _, out _).Should().BeTrue();
            defaulted.Should().Be(specification.DefaultMaximum);

            var minimum = new DefaultHttpContext();
            minimum.Request.QueryString = new QueryString("?maximum=" + specification.MinimumMaximum.Value);
            GatewayAdminEndpointRouteBuilderExtensions.TryPage(minimum, paged with { Pagination = specification },
                out int parsedMinimum, out _, out _).Should().BeTrue();
            parsedMinimum.Should().Be(specification.MinimumMaximum);

            var maximum = new DefaultHttpContext();
            maximum.Request.QueryString = new QueryString("?maximum=" + specification.MaximumMaximum.Value);
            GatewayAdminEndpointRouteBuilderExtensions.TryPage(maximum, paged with { Pagination = specification },
                out int parsedMaximum, out _, out _).Should().BeTrue();
            parsedMaximum.Should().Be(specification.MaximumMaximum);

            var below = new DefaultHttpContext();
            below.Request.QueryString = new QueryString("?maximum=" + (specification.MinimumMaximum.Value - 1));
            GatewayAdminEndpointRouteBuilderExtensions.TryPage(below, paged with { Pagination = specification },
                out _, out _, out _).Should().BeFalse();

            var above = new DefaultHttpContext();
            above.Request.QueryString = new QueryString("?maximum=" + (specification.MaximumMaximum.Value + 1));
            GatewayAdminEndpointRouteBuilderExtensions.TryPage(above, paged with { Pagination = specification },
                out _, out _, out _).Should().BeFalse();
        }

        var utf8Boundary = new DefaultHttpContext();
        utf8Boundary.Request.QueryString = new QueryString("?cursor=" + new string('é', 2048));
        GatewayAdminEndpointRouteBuilderExtensions.TryPage(utf8Boundary, paged,
            out _, out string? acceptedCursor, out _).Should().BeTrue();
        System.Text.Encoding.UTF8.GetByteCount(acceptedCursor!).Should().Be(4096);

        var utf8Oversize = new DefaultHttpContext();
        utf8Oversize.Request.QueryString = new QueryString("?cursor=" + new string('é', 2049));
        GatewayAdminEndpointRouteBuilderExtensions.TryPage(utf8Oversize, paged,
            out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Parameter_projection_follows_each_managed_byte_bound_without_fallbacks()
    {
        GatewayAdminClientParameterConstraint baseline = GatewayAdminClientSemanticLedger.For("submit-and-activate")
            .ParameterConstraints.Single(static value => value.Brand == GatewayAdminClientStringBrand.DesiredStateToken);
        GatewayAdminClientConstraintRules[] variants =
        [
            baseline.Rules with { MinimumUtf8Bytes = 2 },
            baseline.Rules with { MaximumUtf8Bytes = 511 },
        ];

        foreach (GatewayAdminClientConstraintRules rules in variants)
        {
            GatewayAdminClientParameterConstraint constraint = baseline with { Rules = rules };
            var schema = GatewayAdminOpenApiDocumentTransformer.ParameterStringSchema(constraint);
            schema.MinLength.Should().Be(rules.MinimumUtf8Bytes + 2);
            schema.MaxLength.Should().Be(rules.MaximumUtf8Bytes + 2);
            schema.Pattern.Should().Contain($"{{{rules.MinimumUtf8Bytes},{rules.MaximumUtf8Bytes}}}");
            string description = GatewayAdminOpenApiDocumentTransformer.ParameterDescription(constraint);
            description.Should().Contain($"{rules.MinimumUtf8Bytes}-{rules.MaximumUtf8Bytes}");
        }

        GatewayAdminClientParameterConstraint resource = GatewayAdminClientSemanticLedger.For("desired")
            .ParameterConstraints.Single(static value => value.Brand == GatewayAdminClientStringBrand.NamespaceId);
        resource = resource with { Rules = resource.Rules with { MinimumUtf8Bytes = 2, MaximumUtf8Bytes = 127 } };
        var resourceSchema = GatewayAdminOpenApiDocumentTransformer.ParameterStringSchema(resource);
        resourceSchema.MinLength.Should().Be(1);
        resourceSchema.MaxLength.Should().Be(127);
        resourceSchema.Pattern.Should().Contain("{1,127}");
        System.Text.RegularExpressions.Regex.IsMatch("é", resourceSchema.Pattern!).Should().BeTrue();
        System.Text.Encoding.UTF8.GetByteCount("é").Should().Be(2);
        GatewayAdminOpenApiDocumentTransformer.ParameterDescription(resource)
            .Should().Contain("2-127").And.NotContain("1-128");

        GatewayAdminClientParameterConstraint visible = GatewayAdminClientSemanticLedger.For("submit")
            .ParameterConstraints.Single(static value => value.Brand == GatewayAdminClientStringBrand.IdempotencyKey);
        visible = visible with { Rules = visible.Rules with { MinimumUtf8Bytes = 2, MaximumUtf8Bytes = 127 } };
        GatewayAdminOpenApiDocumentTransformer.ParameterDescription(visible)
            .Should().Contain("2-127").And.NotContain("1-128");
    }

    [Fact]
    public void Malformed_parameter_constraints_fail_closed_before_projection()
    {
        GatewayAdminClientParameterConstraint correlation = GatewayAdminClientSemanticLedger.For("capabilities")
            .ParameterConstraints.Single();
        GatewayAdminClientParameterConstraint missingBound = correlation with
        {
            Rules = correlation.Rules with { MaximumUtf8Bytes = null },
        };
        Action projectMissingBound = () => GatewayAdminOpenApiDocumentTransformer.ParameterStringSchema(missingBound);
        projectMissingBound.Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint optionalPath = correlation with
        {
            Location = GatewayAdminClientParameterLocation.Path,
            Required = false,
        };
        Action validateOptionalPath = optionalPath.Validate;
        validateOptionalPath.Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint invalidRange = correlation with
        {
            Rules = correlation.Rules with { MinimumUtf8Bytes = 129, MaximumUtf8Bytes = 128 },
        };
        ((Action)invalidRange.Validate).Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint aboveStringCeiling = correlation with
        {
            Rules = correlation.Rules with
            {
                MaximumUtf8Bytes = GatewayAdminClientParameterConstraint.MaximumOrdinaryStringUtf8Bytes + 1,
            },
        };
        ((Action)aboveStringCeiling.Validate).Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint integerOverflow = correlation with
        {
            Brand = GatewayAdminClientStringBrand.DesiredStateToken,
            Name = "If-Match",
            Rules = correlation.Rules with
            {
                CharacterSet = GatewayAdminClientCharacterSet.StrongEntityTag,
                MaximumUtf8Bytes = int.MaxValue,
            },
        };
        ((Action)(() => GatewayAdminOpenApiDocumentTransformer.ParameterStringSchema(integerOverflow)))
            .Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint invalidCardinality = correlation with
        {
            Rules = correlation.Rules with
            {
                Cardinality = GatewayAdminClientCardinality.Single,
                CollectionMaximum = 2,
            },
        };
        ((Action)invalidCardinality.Validate).Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint aboveCollectionCeiling = correlation with
        {
            Rules = correlation.Rules with
            {
                Cardinality = GatewayAdminClientCardinality.Multiple,
                CollectionMaximum = GatewayAdminClientParameterConstraint.MaximumCollectionItems + 1,
            },
        };
        ((Action)aboveCollectionCeiling.Validate).Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint invalidBrandTarget = correlation with
        {
            Brand = GatewayAdminClientStringBrand.ContinuationToken,
        };
        ((Action)invalidBrandTarget.Validate).Should().Throw<InvalidOperationException>();

        GatewayAdminClientParameterConstraint invalidCharacterSet = correlation with
        {
            Rules = correlation.Rules with { CharacterSet = GatewayAdminClientCharacterSet.StrongEntityTag },
        };
        ((Action)invalidCharacterSet.Validate).Should().Throw<InvalidOperationException>();

        ImmutableArray<GatewayAdminClientParameterConstraint> duplicateTargets = [correlation, correlation];
        Action validateDuplicates = () => GatewayAdminClientParameterConstraintValidator.Validate(duplicateTargets);
        validateDuplicates.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Endpoint_ledger_maps_one_static_capability_and_exact_resource_policy_per_scope()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHpdGateway(static gateway => gateway.AddCoreFamilies());
        builder.Services.AddHpdGatewayManagement();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", null);
        builder.Services.AddAuthorization(options =>
        {
            foreach (string policy in GatewayAdminCapabilities.All)
                options.AddPolicy(policy, value => value.RequireAssertion(static _ => true));
            options.AddPolicy(GatewayAdminResourcePolicies.Namespace, value => value.RequireAssertion(static _ => true));
            options.AddPolicy(GatewayAdminResourcePolicies.Target, value => value.RequireAssertion(static _ => true));
            options.AddPolicy(GatewayAdminResourcePolicies.Administration, value => value.RequireAssertion(static _ => true));
        });
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("gateway-management", value =>
        {
            value.PermitLimit = 8;
            value.QueueLimit = 0;
            value.Window = TimeSpan.FromSeconds(1);
        }));
        builder.Services.AddRequestTimeouts(options => options.AddPolicy("gateway-management", TimeSpan.FromSeconds(5)));
        builder.Services.AddSingleton<IGatewayAdminActorProjector, TestActorProjector>();
        builder.Services.AddHpdGatewayAdmin();
        WebApplication application = builder.Build();
        ImmutableDictionary<string, string> policies = GatewayAdminCapabilities.All
            .ToImmutableDictionary(static value => value, static value => value, StringComparer.Ordinal);

        application.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
        {
            AuthenticationScheme = "test",
            OpenApiSecurityScheme = "test",
            CapabilityPolicies = policies,
        });

        RouteEndpoint[] endpoints = ((IEndpointRouteBuilder)application).DataSources.SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(static endpoint => endpoint.Metadata.GetMetadata<GatewayAdminEndpointDescriptor>() is not null)
            .ToArray();
        endpoints.Should().HaveCount(GatewayAdminEndpointLedger.V1.Length);
        GatewayAdminEndpointLedger.V1.Should().HaveCount(23);
        GatewayAdminEndpointLedger.V1.Select(static endpoint => (endpoint.Method, endpoint.Pattern))
            .Should().OnlyHaveUniqueItems();
        foreach (RouteEndpoint endpoint in endpoints)
        {
            GatewayAdminEndpointDescriptor descriptor = endpoint.Metadata.GetRequiredMetadata<GatewayAdminEndpointDescriptor>();
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Count(value => StringComparer.Ordinal.Equals(value.Policy, descriptor.Capability))
                .Should().Be(1);
            descriptor.ResourceKind.HasValue.Should().Be(descriptor.ResourcePolicy is not null);
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Any(metadata => (metadata.StatusCode == 200 || metadata.StatusCode == 201 || metadata.StatusCode == 202) && metadata.Type != null)
                .Should().BeTrue();
            if (descriptor.Method == "POST" && descriptor.Operation != "provision")
                endpoint.Metadata.GetMetadata<IAcceptsMetadata>().Should().NotBeNull();
        }
    }

    [Fact]
    public void Endpoint_role_validation_rejects_duplicate_metadata()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        WebApplication app = builder.Build();
        app.MapGet("/duplicate", static () => "bad")
            .WithName("HpdGatewayDuplicate")
            .WithHpdGatewayEndpointRole(GatewayListenerRole.Management, "admin")
            .WithHpdGatewayEndpointRole(GatewayListenerRole.DataPlane, "data");

        Action validate = () => app.ValidateHpdGatewayEndpointRoles();
        validate.Should().Throw<InvalidOperationException>().WithMessage("*exactly one listener role*");
    }

    [Fact]
    public void Wire_contract_has_no_unbounded_or_reflection_escape_types()
    {
        Type[] dtoTypes =
        [
            typeof(GatewayRevisionRequest), typeof(GatewayActivationRequest), typeof(GatewayCompareRequest),
            typeof(GatewayImportRequest), typeof(GatewayBackupRequest), typeof(GatewayPurgeRequest),
            typeof(GatewayOperationResponse), typeof(GatewayActivationHistoryResponse),
            typeof(GatewayTargetStatusResponse), typeof(GatewayExportResponse),
            typeof(GatewayAdministrativeResponse),
        ];
        dtoTypes.SelectMany(static type => type.GetProperties())
            .Should().NotContain(property => property.PropertyType == typeof(object) ||
                property.PropertyType == typeof(System.Text.Json.JsonElement) ||
                property.PropertyType == typeof(Type) || typeof(Delegate).IsAssignableFrom(property.PropertyType));
        typeof(GatewayAdminEndpointRouteBuilderExtensions).GetMethods()
            .Should().ContainSingle(method => method.Name == nameof(GatewayAdminEndpointRouteBuilderExtensions.MapHpdGatewayAdmin));
    }

    [Fact]
    public void Mapping_rejects_an_incomplete_capability_policy_catalog()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHpdGateway(static gateway => gateway.AddCoreFamilies());
        builder.Services.AddHpdGatewayManagement();
        builder.Services.AddHpdGatewayAdmin();
        builder.Services.AddSingleton<IGatewayAdminActorProjector, TestActorProjector>();
        WebApplication application = builder.Build();
        Action map = () => application.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
        {
            OpenApiSecurityScheme = "test",
            CapabilityPolicies = ImmutableDictionary<string, string>.Empty,
        });
        map.Should().Throw<InvalidOperationException>().WithMessage("*exact v1 catalog*");
    }

    [Fact]
    public void HpdAuth_bridge_rejects_split_brain_endpoint_profile_options()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHpdGateway(static gateway => gateway.AddCoreFamilies());
        builder.Services.AddHpdGatewayManagement();
        builder.Services.AddHpdGatewayAdmin();
        builder.Services.AddHPDControlPlane(options =>
        {
            options.AddProfile("gateway", profile =>
            {
                profile.AuthenticationScheme = "hpd-auth";
                profile.AuthenticationProfile = "gateway";
                profile.ActorIdentifierClaim = ClaimTypes.NameIdentifier;
                profile.RateLimitPolicy = "hpd-rate";
                profile.RequestTimeoutPolicy = "hpd-timeout";
                profile.OpenApiSecurityScheme = "Bearer";
            });
            foreach (string capability in GatewayAdminCapabilities.All)
                options.MapCapability(capability, "hpd-policy");
        });
        builder.Services.AddHpdGatewayAdminHpdAuth("gateway");
        builder.Services.Last(descriptor => descriptor.ServiceType == typeof(IGatewayAdminActorProjector))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
        builder.Services.Last(descriptor => descriptor.ServiceType == typeof(IGatewayAdminSecurityMetadataProvider))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);
        WebApplication application = builder.Build();
        Action map = () => application.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
        {
            AuthenticationScheme = "different",
            OpenApiSecurityScheme = "Bearer",
            RateLimitPolicy = "hpd-rate",
            RequestTimeoutPolicy = "hpd-timeout",
            CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
                static capability => capability, static _ => "hpd-policy", StringComparer.Ordinal),
        });
        map.Should().Throw<InvalidOperationException>().WithMessage("*do not match*");
    }

    private sealed class TestActorProjector : IGatewayAdminActorProjector
    {
        public ValueTask<GatewayAdminRequestAttribution> ProjectAsync(
            HttpContext context, string capability, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GatewayAdminRequestAttribution("actor", "test", capability, "correlation"));
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
