using System.Collections.Immutable;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using FluentAssertions;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.Base;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayAdminHttpTests
{
    [Fact]
    public async Task Rollback_activates_the_selected_immutable_revision_through_the_real_admin_surface()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        HttpClient client = application.GetTestClient();

        using (var provision = new HttpRequestMessage(HttpMethod.Post,
            "/management/gateway/v1/namespaces/ns/targets/node:provision"))
        {
            provision.Headers.Add("Idempotency-Key", "provision-rollback-test");
            (await client.SendAsync(provision)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        string configurationJson = JsonSerializer.Serialize(new GatewayConfiguration
        {
            SchemaVersion = new(1, 0),
            CanonicalizationVersion = 1,
        }, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        var submissionBody = new GatewayRevisionRequest
        {
            ConfigurationJson = configurationJson,
            SourceKind = "test",
            SourceId = "rollback-test",
        };
        using var submission = new HttpRequestMessage(HttpMethod.Post,
            "/management/gateway/v1/namespaces/ns/targets/node/revisions:submitAndActivate");
        submission.Headers.Add("Idempotency-Key", "submit-rollback-test");
        submission.Content = new StringContent(
            JsonSerializer.Serialize(submissionBody, GatewayAdminJsonContext.Default.GatewayRevisionRequest),
            Encoding.UTF8, "application/json");
        HttpResponseMessage submittedResponse = await client.SendAsync(submission);
        submittedResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        GatewayRevisionResponse submitted = JsonSerializer.Deserialize(
            await submittedResponse.Content.ReadAsStringAsync(),
            GatewayAdminJsonContext.Default.GatewayRevisionResponse)!;

        using var rollback = new HttpRequestMessage(HttpMethod.Post,
            $"/management/gateway/v1/namespaces/ns/targets/node/revisions/{submitted.RevisionId}:rollback");
        rollback.Headers.Add("Idempotency-Key", "rollback-test");
        rollback.Headers.TryAddWithoutValidation("If-Match", $"\"{submitted.DesiredStateToken}\"");
        rollback.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        HttpResponseMessage rollbackResponse = await client.SendAsync(rollback);
        rollbackResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        GatewayRevisionResponse rolledBack = JsonSerializer.Deserialize(
            await rollbackResponse.Content.ReadAsStringAsync(),
            GatewayAdminJsonContext.Default.GatewayRevisionResponse)!;

        rolledBack.RevisionId.Should().Be(submitted.RevisionId);
        rolledBack.ActivationIntentId.Should().NotBeNull().And.NotBe(submitted.ActivationIntentId);
        rolledBack.DesiredStateToken.Should().NotBeNull().And.NotBe(submitted.DesiredStateToken);

        HttpResponseMessage receiptResponse = await client.GetAsync(
            $"/management/gateway/v1/namespaces/ns/operations/{rolledBack.OperationId}");
        receiptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        GatewayOperationProjection receipt = JsonSerializer.Deserialize(
            await receiptResponse.Content.ReadAsStringAsync(),
            GatewayAdminJsonContext.Default.GatewayOperationProjection)!;
        GatewayCommandOperationProjection commandReceipt = receipt.Should().BeOfType<GatewayCommandOperationProjection>().Subject;
        commandReceipt.Operation.Should().Be("rollback");
        commandReceipt.DesiredStateToken.Should().Be(rolledBack.DesiredStateToken);
    }

    [Fact]
    public async Task Capability_endpoint_requires_exact_management_listener_identity()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        HttpClient client = application.GetTestClient();

        HttpResponseMessage accepted = await client.GetAsync("/management/gateway/v1/capabilities");
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/management/gateway/v1/capabilities");
        wrong.Headers.Add("x-test-listener", "data");
        HttpResponseMessage rejected = await client.SendAsync(wrong);
        rejected.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await rejected.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Governed_composition_automatically_rejects_invalid_listener_role_graph_at_startup()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        application.MapGet("/invalid-role", static () => "invalid")
            .WithName("HpdGatewayInvalidRole");

        Func<Task> start = () => application.StartAsync();
        await start.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exactly one listener role*");
    }

    [Fact]
    public async Task Resource_denial_is_safe_not_found_before_body_or_authority_resolution()
    {
        await using WebApplication application = Build(resourceAllowed: false);
        await application.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/management/gateway/v1/namespaces/ns/targets/node:provision");
        request.Headers.Add("Idempotency-Key", "key");
        request.Content = new StringContent("not-json");
        HttpResponseMessage response = await application.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("gateway.admin.resource.notFound");
        body.ToLowerInvariant().Should().NotContain("policy");
    }

    [Fact]
    public async Task Body_contract_rejects_missing_media_type_and_oversize_with_gateway_envelopes()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        HttpClient client = application.GetTestClient();

        using var missingType = new HttpRequestMessage(HttpMethod.Post, "/management/gateway/v1/candidates:validate")
        {
            Content = new ByteArrayContent("{}"u8.ToArray()),
        };
        HttpResponseMessage unsupported = await client.SendAsync(missingType);
        unsupported.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await unsupported.Content.ReadAsStringAsync()).Should().Contain("gateway.admin.media.unsupported");

        using var oversize = new HttpRequestMessage(HttpMethod.Post, "/management/gateway/v1/candidates:validate")
        {
            Content = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]),
        };
        oversize.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        HttpResponseMessage tooLarge = await client.SendAsync(oversize);
        tooLarge.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await tooLarge.Content.ReadAsStringAsync()).Should().Contain("gateway.admin.request.tooLarge");
    }

    [Fact]
    public async Task Host_capabilities_and_validation_share_exact_advisory_snapshot_identity()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        HttpClient client = application.GetTestClient();

        HttpResponseMessage hostResponse = await client.GetAsync("/management/gateway/v1/host-capabilities");
        hostResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument host = JsonDocument.Parse(await hostResponse.Content.ReadAsStringAsync());
        string hostAlgorithm = host.RootElement.GetProperty("snapshotAlgorithm").GetString()!;
        string hostValue = host.RootElement.GetProperty("snapshotValue").GetString()!;
        hostAlgorithm.Should().Be("sha-256");
        hostValue.Should().HaveLength(64);
        host.RootElement.GetProperty("capabilities").GetProperty("installedFamilies")
            .GetArrayLength().Should().BeGreaterThan(0);

        var configuration = new GatewayConfiguration
        {
            SchemaVersion = new(1, 0),
            CanonicalizationVersion = 1,
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            configuration,
            GatewayJsonSerializerContext.Default.GatewayConfiguration);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/management/gateway/v1/candidates:validate")
        {
            Content = new ByteArrayContent(json),
        };
        request.Headers.Add("X-Correlation-ID", "validation-correlation");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/hpd.gateway+json");
        HttpResponseMessage validationResponse = await client.SendAsync(request);
        validationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument validation = JsonDocument.Parse(await validationResponse.Content.ReadAsStringAsync());
        validation.RootElement.GetProperty("isValid").GetBoolean().Should().BeTrue();
        validation.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        validation.RootElement.GetProperty("canonicalizationVersion").GetString().Should().Be("1");
        validation.RootElement.GetProperty("contentHashAlgorithm").GetString().Should().Be("sha-256");
        validation.RootElement.GetProperty("contentHashValue").GetString().Should().HaveLength(64);
        validation.RootElement.GetProperty("hostCapabilitySnapshotAlgorithm").GetString().Should().Be(hostAlgorithm);
        validation.RootElement.GetProperty("hostCapabilitySnapshotValue").GetString().Should().Be(hostValue);
        validation.RootElement.GetProperty("correlationId").GetString().Should().Be("validation-correlation");
        validation.RootElement.GetProperty("observedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        validation.RootElement.TryGetProperty("canonicalJson", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Rejected_validation_returns_snapshot_evidence_without_fabricated_canonical_identity()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/management/gateway/v1/candidates:validate")
        {
            Content = new ByteArrayContent("{}"u8.ToArray()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response = await application.GetTestClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("isValid").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("diagnostics").GetArrayLength().Should().BeGreaterThan(0);
        document.RootElement.GetProperty("schemaVersion").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("canonicalizationVersion").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("contentHashAlgorithm").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("contentHashValue").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("hostCapabilitySnapshotValue").GetString().Should().HaveLength(64);
    }

    [Fact]
    public async Task Provisioned_inactive_target_has_honest_management_status_without_node_observation()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        GatewayManagementCommandResult provisioned = await application.Services
            .GetRequiredService<IGatewayManagementCommandCoordinator>()
            .ProvisionLocalTargetAsync(new("ns", "node", "provision", new("actor", "test", "policy"), "correlation"));
        provisioned.IsAccepted.Should().BeTrue(provisioned.Code);

        HttpResponseMessage response = await application.GetTestClient().GetAsync(
            "/management/gateway/v1/namespaces/ns/targets/node/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("nodeObservation").GetString().Should().Be("NotAttempted");
        document.RootElement.GetProperty("node").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(GatewayNodeOutcomeKind.RejectedBeforePublish, "ObservedWithoutEffectiveProjection")]
    [InlineData(GatewayNodeOutcomeKind.PublicationIndeterminate, "Indeterminate")]
    public async Task Failed_or_indeterminate_activation_is_not_reported_as_never_attempted(
        GatewayNodeOutcomeKind outcome, string expectedObservation)
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        GatewayManagementCommandResult provisioned = await application.Services
            .GetRequiredService<IGatewayManagementCommandCoordinator>()
            .ProvisionLocalTargetAsync(new("ns", "node", "provision", new("actor", "test", "policy"), "correlation"));
        provisioned.IsAccepted.Should().BeTrue(provisioned.Code);
        BaseSession session = application.Services.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "gateway-admin-status-test",
            AuthSource = GatewayManagementBasePolicy.TrustedSource,
        }, options => options.Mode = OperationMode.System);
        const string activationIntentId = "gwm.activation-intent.status-test";
        (await session.Collection(GatewayDesiredState.Collection).CreateAsync(
            GatewayAuthorityRecordIds.DesiredState("local", "node"),
            new GatewayDesiredState
            {
                ManagementAuthorityId = "local",
                TargetNodeId = "node",
                NamespaceId = "ns",
                ActivationIntentId = activationIntentId,
                RevisionId = "gwm.revision.status-test",
                CandidateId = "candidate-status-test",
            })).RequireValue();
        (await session.Collection(GatewayNodeActivationOutcome.Collection).CreateAsync(
            RecordId.Create("gwm.node-outcome.status-test-" + outcome.ToString().ToLowerInvariant()),
            new GatewayNodeActivationOutcome
            {
                NamespaceId = "ns",
                TargetNodeId = "node",
                ActivationIntentId = activationIntentId,
                AuthorityId = "authority",
                AuthorityEpoch = "epoch",
                AuthorityVersion = 1,
                Kind = outcome,
                Code = "test." + outcome,
            })).RequireValue();

        HttpResponseMessage response = await application.GetTestClient().GetAsync(
            "/management/gateway/v1/namespaces/ns/targets/node/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("nodeObservation").GetString().Should().Be(expectedObservation);
        document.RootElement.GetProperty("node").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Historical_rejection_and_attempt_do_not_describe_unattempted_current_desire()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        GatewayManagementCommandResult provisioned = await application.Services
            .GetRequiredService<IGatewayManagementCommandCoordinator>()
            .ProvisionLocalTargetAsync(new("ns", "node", "provision", new("actor", "test", "policy"), "correlation"));
        provisioned.IsAccepted.Should().BeTrue(provisioned.Code);
        BaseSession session = application.Services.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "gateway-admin-generation-test",
            AuthSource = GatewayManagementBasePolicy.TrustedSource,
        }, options => options.Mode = OperationMode.System);
        (await session.Collection(GatewayDesiredState.Collection).CreateAsync(
            GatewayAuthorityRecordIds.DesiredState("local", "node"),
            new GatewayDesiredState
            {
                ManagementAuthorityId = "local",
                TargetNodeId = "node",
                NamespaceId = "ns",
                ActivationIntentId = "gwm.activation-intent.desired-b",
                RevisionId = "gwm.revision.desired-b",
                CandidateId = "candidate-desired-b",
            })).RequireValue();
        (await session.Collection(GatewayNodeActivationOutcome.Collection).CreateAsync(
            RecordId.Create("gwm.node-outcome.historical-a"),
            new GatewayNodeActivationOutcome
            {
                NamespaceId = "ns",
                TargetNodeId = "node",
                ActivationIntentId = "gwm.activation-intent.historical-a",
                AuthorityId = "authority",
                AuthorityEpoch = "epoch",
                AuthorityVersion = 1,
                Kind = GatewayNodeOutcomeKind.RejectedBeforePublish,
                Code = "historical.rejected",
            })).RequireValue();
        (await session.Collection(GatewayDeliveryOutboxItem.Collection).CreateAsync(
            RecordId.Create("gwm.outbox.historical-a"),
            new GatewayDeliveryOutboxItem
            {
                NamespaceId = "ns",
                TargetNodeId = "node",
                ActivationIntentId = "gwm.activation-intent.historical-a",
                State = GatewayDeliveryState.TerminalFailure,
                AttemptCount = 1,
            })).RequireValue();

        HttpResponseMessage status = await application.GetTestClient().GetAsync(
            "/management/gateway/v1/namespaces/ns/targets/node/status");
        using JsonDocument statusDocument = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        statusDocument.RootElement.GetProperty("nodeObservation").GetString().Should().Be("NotAttempted");

        HttpResponseMessage history = await application.GetTestClient().GetAsync(
            "/management/gateway/v1/namespaces/ns/targets/node/activations");
        using JsonDocument historyDocument = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        historyDocument.RootElement.GetProperty("outcomes").GetProperty("items")[0]
            .GetProperty("code").GetString().Should().Be("historical.rejected");
    }

    [Fact]
    public async Task Generated_openapi_contains_the_complete_typed_ledger()
    {
        await using WebApplication application = Build(resourceAllowed: true, mapOpenApi: true);
        await application.StartAsync();
        string json = await application.GetTestClient().GetStringAsync("/openapi/hpd-gateway-v1.json");
        using JsonDocument document = JsonDocument.Parse(json);
        GatewayClientGenerationSnapshotV1 snapshot = GatewayClientGenerationSnapshotV1.Create(
            Encoding.UTF8.GetBytes(json), "test");
        snapshot.Manifest.Operations.Should().HaveCount(23);
        snapshot.Manifest.SchemaConstraints.Should().HaveCount(GatewayAdminClientSchemaConstraintLedger.V1.Length);
        snapshot.OpenApiSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        snapshot.ManifestSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        snapshot.SourceSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        GatewayDeclarationEditorLedgerExportDocument editorLedger =
            GatewayDeclarationEditorLedgerExporter.Export(JsonNode.Parse(json)!.AsObject());
        editorLedger.Value.Envelope.Records.Should().HaveCount(420);
        editorLedger.Value.EnvelopeSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        if (Environment.GetEnvironmentVariable("HPD_GATEWAY_SNAPSHOT_OUT") is { Length: > 0 } snapshotOutput)
            await File.WriteAllBytesAsync(snapshotOutput, snapshot.SnapshotUtf8.ToArray());
        if (Environment.GetEnvironmentVariable("HPD_GATEWAY_EDITOR_LEDGER_OUT") is { Length: > 0 } editorOutput)
            await File.WriteAllBytesAsync(editorOutput, editorLedger.Utf8.ToArray());
        GatewayClientGenerationSnapshotV1.Create(Encoding.UTF8.GetBytes(json), "test").SourceSha256
            .Should().Be(snapshot.SourceSha256);
        foreach (int schemaCount in new[] { 257, 512 })
        {
            JsonObject boundedDocument = JsonNode.Parse(json)!.AsObject();
            JsonObject schemas = boundedDocument["components"]!["schemas"]!.AsObject();
            for (int index = schemas.Count; index < schemaCount; index++)
                schemas.Add($"Synthetic_{index:D3}", new JsonObject { ["type"] = "string" });
            GatewayClientGenerationSnapshotV1 bounded = GatewayClientGenerationSnapshotV1.Create(
                Encoding.UTF8.GetBytes(boundedDocument.ToJsonString()), "test");
            using JsonDocument envelope = JsonDocument.Parse(bounded.SnapshotUtf8.ToArray());
            envelope.RootElement.GetProperty("openApi").GetProperty("components").GetProperty("schemas")
                .EnumerateObject().Should().HaveCount(schemaCount);
        }
        JsonObject oversizedDocument = JsonNode.Parse(json)!.AsObject();
        JsonObject oversizedSchemas = oversizedDocument["components"]!["schemas"]!.AsObject();
        for (int index = oversizedSchemas.Count; index < 513; index++)
            oversizedSchemas.Add($"Synthetic_{index:D3}", new JsonObject { ["type"] = "string" });
        FluentActions.Invoking(() => GatewayClientGenerationSnapshotV1.Create(
                Encoding.UTF8.GetBytes(oversizedDocument.ToJsonString()), "test"))
            .Should().Throw<InvalidOperationException>();
        foreach (string invalid in new[] { "\uD800", "\uDC00" })
        {
            string escaped = invalid == "\uD800" ? "\\uD800" : "\\uDC00";
            string malformedValue = json.Replace("HPD.Gateway Admin API", escaped, StringComparison.Ordinal);
            FluentActions.Invoking(() => GatewayClientGenerationSnapshotV1.Create(
                    Encoding.UTF8.GetBytes(malformedValue), "test"))
                .Should().Throw<InvalidOperationException>().WithMessage("*lone UTF-16 surrogates*");
            string malformedKey = json[..^1] + $",\"{escaped}\":true}}";
            FluentActions.Invoking(() => GatewayClientGenerationSnapshotV1.Create(
                    Encoding.UTF8.GetBytes(malformedKey), "test"))
                .Should().Throw<InvalidOperationException>().WithMessage("*lone UTF-16 surrogates*");
        }
        JsonObject reordered = new();
        foreach ((string key, JsonNode? value) in JsonNode.Parse(json)!.AsObject().Reverse())
            reordered.Add(key, value?.DeepClone());
        GatewayClientGenerationSnapshotV1.Create(Encoding.UTF8.GetBytes(reordered.ToJsonString()), "test").SourceSha256
            .Should().Be(snapshot.SourceSha256, "OpenAPI object order is non-behavioral");

        JsonObject presentationDrift = JsonNode.Parse(json)!.AsObject();
        presentationDrift["info"]!["title"] = "Changed presentation";
        GatewayClientGenerationSnapshotV1 changed = GatewayClientGenerationSnapshotV1.Create(
            Encoding.UTF8.GetBytes(presentationDrift.ToJsonString()), "test");
        changed.ManifestSha256.Should().Be(snapshot.ManifestSha256);
        changed.OpenApiSha256.Should().NotBe(snapshot.OpenApiSha256);
        changed.SourceSha256.Should().NotBe(snapshot.SourceSha256);

        const string decomposedPresentation = "Gate\u0301way presentation";
        JsonObject nonNfcPresentation = JsonNode.Parse(json)!.AsObject();
        nonNfcPresentation["info"]!["title"] = decomposedPresentation;
        GatewayClientGenerationSnapshotV1 nonNfcSnapshot = GatewayClientGenerationSnapshotV1.Create(
            Encoding.UTF8.GetBytes(nonNfcPresentation.ToJsonString()), "test");
        nonNfcSnapshot.SourceSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        nonNfcSnapshot.SourceSha256.Should().NotBe(snapshot.SourceSha256);
        using (JsonDocument nonNfcEnvelope = JsonDocument.Parse(nonNfcSnapshot.SnapshotUtf8.ToArray()))
            nonNfcEnvelope.RootElement.GetProperty("openApi").GetProperty("info").GetProperty("title")
                .GetString().Should().Be(decomposedPresentation);

        JsonObject securityDrift = JsonNode.Parse(json)!.AsObject();
        securityDrift["components"]!["securitySchemes"]!["test"]!["in"] = "header";
        FluentActions.Invoking(() => GatewayClientGenerationSnapshotV1.Create(
                Encoding.UTF8.GetBytes(securityDrift.ToJsonString()), "test"))
            .Should().Throw<InvalidOperationException>();
        JsonObject operationDrift = JsonNode.Parse(json)!.AsObject();
        operationDrift["paths"]!["/management/gateway/v1/capabilities"]!["get"]!["operationId"] = "drift";
        FluentActions.Invoking(() => GatewayClientGenerationSnapshotV1.Create(
                Encoding.UTF8.GetBytes(operationDrift.ToJsonString()), "test"))
            .Should().Throw<InvalidOperationException>();
        JsonElement securityScheme = document.RootElement.GetProperty("components")
            .GetProperty("securitySchemes").GetProperty("test");
        securityScheme.GetProperty("type").GetString().Should().Be("http");
        securityScheme.GetProperty("scheme").GetString().Should().Be("bearer");
        securityScheme.GetProperty("bearerFormat").GetString().Should().Be("JWT");
        securityScheme.TryGetProperty("in", out _).Should().BeFalse();
        securityScheme.EnumerateObject().Select(static property => property.Name)
            .Should().BeEquivalentTo("type", "scheme", "bearerFormat");
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.EnumerateObject().SelectMany(static path => path.Value.EnumerateObject())
            .Should().HaveCount(23);
        foreach (GatewayAdminEndpointDescriptor descriptor in GatewayAdminEndpointLedger.V1)
        {
            GatewayAdminClientOperationSemantics semantics = GatewayAdminClientSemanticLedger.For(descriptor.Operation);
            JsonElement operation = paths
                .GetProperty("/management/gateway/v1" + descriptor.Pattern)
                .GetProperty(descriptor.Method.ToLowerInvariant());
            operation.GetProperty("operationId").GetString().Should().Be("HpdGatewayAdmin." + descriptor.Operation);
            operation.GetProperty("security")[0].TryGetProperty("test", out _).Should().BeTrue();

            JsonElement[] parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
            string[] pathNames = descriptor.Pattern.Split('{').Skip(1)
                .Select(static segment => segment[..segment.IndexOf('}')]).ToArray();
            parameters.Where(static parameter => parameter.GetProperty("in").GetString() == "path")
                .Select(static parameter => parameter.GetProperty("name").GetString())
                .Should().Equal(pathNames);
            foreach (string pathName in pathNames)
                AssertParameter(parameters, pathName, "path", required: true, maximumLength: 128,
                    minimumLength: 1, pattern: "^[^\\u0000-\\u001F\\u007F-\\u009F]{1,128}$", descriptionContains: "128 UTF-8 bytes");
            AssertParameter(parameters, "X-Correlation-ID", "header", required: false, maximumLength: 128,
                minimumLength: 1, pattern: "^[!-~]{1,128}$");
            if (semantics.Idempotency == GatewayAdminClientIdempotency.Required)
                AssertParameter(parameters, "Idempotency-Key", "header", required: true, maximumLength: 128,
                    minimumLength: 1, pattern: "^[!-~]{1,128}$");
            else
                parameters.Should().NotContain(parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key");
            bool ifMatch = semantics.DesiredPrecondition == GatewayAdminClientDesiredPrecondition.CreateOrReplace;
            if (ifMatch) AssertParameter(parameters, "If-Match", "header", required: false, maximumLength: 514,
                minimumLength: 3, pattern: "^\"(?=[!-~]{1,512}\"$)[^\",]+\"$");
            else parameters.Should().NotContain(parameter => parameter.GetProperty("name").GetString() == "If-Match");
            bool paged = semantics.Pagination.Kind == GatewayAdminClientPaginationKind.OpaqueCursor;
            if (paged)
            {
                AssertParameter(parameters, "maximum", "query", required: false);
                JsonElement maximum = parameters.Single(value => value.GetProperty("name").GetString() == "maximum")
                    .GetProperty("schema");
                maximum.GetProperty("minimum").GetInt32().Should().Be(1);
                maximum.GetProperty("maximum").GetInt32().Should().Be(256);
                maximum.GetProperty("default").GetInt32().Should().Be(64);
                foreach (GatewayAdminClientParameterConstraint cursor in semantics.ParameterConstraints.Where(value =>
                    value.Location == GatewayAdminClientParameterLocation.Query &&
                    value.Brand == GatewayAdminClientStringBrand.ContinuationToken))
                    AssertParameter(parameters, cursor.Name, "query", required: false, maximumLength: 4096);
            }
            else
                parameters.Should().NotContain(parameter => parameter.GetProperty("in").GetString() == "query");

            bool hasBody = semantics.RequestBodyPresence != GatewayAdminClientRequestBodyPresence.None;
            operation.TryGetProperty("requestBody", out JsonElement requestBody).Should().Be(hasBody);
            if (hasBody)
            {
                JsonElement content = requestBody.GetProperty("content");
                content.EnumerateObject().Select(static media => media.Name)
                    .Should().BeEquivalentTo("application/json", "application/hpd.gateway+json");
                content.EnumerateObject().Should().OnlyContain(static media =>
                    media.Value.GetProperty("schema").ValueKind == JsonValueKind.Object);
                bool requestBodyIsRequired = requestBody.TryGetProperty("required", out JsonElement requiredProperty)
                    && requiredProperty.GetBoolean();
                requestBodyIsRequired.Should().Be(
                    semantics.RequestBodyPresence == GatewayAdminClientRequestBodyPresence.Required);
                requestBody.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
            }

            JsonElement responses = operation.GetProperty("responses");
            responses.TryGetProperty(semantics.SuccessStatus.ToString(), out JsonElement success).Should().BeTrue();
            success.GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _).Should().BeTrue();
            foreach (string error in ErrorStatuses(descriptor.Operation))
            {
                responses.TryGetProperty(error, out JsonElement failure).Should().BeTrue($"{descriptor.Operation} declares {error}");
                failure.GetProperty("content").GetProperty("application/json")
                    .TryGetProperty("schema", out _).Should().BeTrue();
            }
        }

        JsonElement revisionSchema = RequestSchema(document.RootElement, paths, "/management/gateway/v1/namespaces/{ns}/targets/{target}/revisions", "post");
        AssertStringProperty(revisionSchema, "configurationJson", 1, 4 * 1024 * 1024, descriptionContains: "UTF-8 bytes");
        AssertStringProperty(revisionSchema, "sourceKind", 1, 128,
            "^[^\\u0000-\\u001F\\u007F-\\u009F]{1,128}$", "128");
        AssertStringProperty(revisionSchema, "sourceId", 1, 128,
            "^[^\\u0000-\\u001F\\u007F-\\u009F]{1,128}$", "128");
        AssertStringProperty(revisionSchema, "description", null, 1024);

        JsonElement activationSchema = RequestSchema(document.RootElement, paths,
            "/management/gateway/v1/namespaces/{ns}/targets/{target}/revisions/{revision}:activate", "post");
        AssertStringProperty(activationSchema, "description", null, 1024);

        JsonElement compareSchema = RequestSchema(document.RootElement, paths,
            "/management/gateway/v1/namespaces/{ns}/targets/{target}/revisions:compare", "post");
        AssertStringProperty(compareSchema, "leftRevisionId", 1, 128,
            "^[^\\u0000-\\u001F\\u007F-\\u009F]{1,128}$", "128");
        AssertStringProperty(compareSchema, "rightRevisionId", 1, 128,
            "^[^\\u0000-\\u001F\\u007F-\\u009F]{1,128}$", "128");

        JsonElement importSchema = RequestSchema(document.RootElement, paths,
            "/management/gateway/v1/namespaces/{ns}/targets/{target}/revisions:import", "post");
        AssertStringProperty(importSchema, "configurationJson", 1, 4 * 1024 * 1024, descriptionContains: "UTF-8 bytes");
        AssertStringProperty(importSchema, "sourceId", 1, 128,
            "^[^\\u0000-\\u001F\\u007F-\\u009F]{1,128}$", "128");
        AssertStringProperty(importSchema, "description", null, 1024);

        JsonElement backupSchema = RequestSchema(document.RootElement, paths,
            "/management/gateway/v1/namespaces/{ns}/administration/backups", "post");
        AssertStringProperty(backupSchema, "sinkName", 1, 128, "^[a-z0-9.-]{1,128}$");
        AssertStringProperty(backupSchema, "artifactLabel", 1, 128, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$");

        JsonElement purgeSchema = RequestSchema(document.RootElement, paths,
            "/management/gateway/v1/namespaces/{ns}/administration/purges", "post");
        JsonElement resourceIds = purgeSchema.GetProperty("properties").GetProperty("resourceIds");
        resourceIds.GetProperty("minItems").GetInt32().Should().Be(1);
        resourceIds.GetProperty("maxItems").GetInt32().Should().Be(256);
        resourceIds.GetProperty("description").GetString().Should().Contain("Ordinal");
        resourceIds.GetProperty("items").GetProperty("description").GetString().Should().Contain("128");
        resourceIds.GetProperty("items").GetProperty("pattern").GetString()
            .Should().Be("^[^\\u0000-\\u001F\\u007F-\\u009F]{1,128}$");

        JsonElement submitResponseSchema = paths
            .GetProperty("/management/gateway/v1/namespaces/{ns}/targets/{target}/revisions")
            .GetProperty("post").GetProperty("responses").GetProperty("201")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        JsonElement responseSchema = ResolveSchema(document.RootElement, submitResponseSchema);
        JsonElement desiredTokenSchema = responseSchema.GetProperty("properties").GetProperty("desiredStateToken");
        desiredTokenSchema.GetProperty("pattern").GetString().Should().Be("^[!-~]{1,512}$");

        const string opaqueToken = "opaque-desired-token";
        byte[] responseJson = JsonSerializer.SerializeToUtf8Bytes(
            new GatewayRevisionResponse("operation-1", "revision-1", "intent-1", opaqueToken, false),
            GatewayAdminJsonContext.Default.GatewayRevisionResponse);
        using JsonDocument serializedResponse = JsonDocument.Parse(responseJson);
        string runtimeToken = serializedResponse.RootElement.GetProperty("desiredStateToken").GetString()!;
        runtimeToken.Should().Be(opaqueToken);
        System.Text.RegularExpressions.Regex.IsMatch(runtimeToken,
            desiredTokenSchema.GetProperty("pattern").GetString()!).Should().BeTrue();
        runtimeToken.Should().NotStartWith("\"").And.NotEndWith("\"");

        JsonElement submitAndActivate = paths
            .GetProperty("/management/gateway/v1/namespaces/{ns}/targets/{target}/revisions:submitAndActivate")
            .GetProperty("post");
        submitAndActivate.GetProperty("parameters").EnumerateArray()
            .Single(static value => value.GetProperty("name").GetString() == "If-Match")
            .GetProperty("schema").GetProperty("pattern").GetString()
            .Should().Be("^\"(?=[!-~]{1,512}\"$)[^\",]+\"$");
    }

    private static JsonElement RequestSchema(
        JsonElement document,
        JsonElement paths,
        string path,
        string method)
    {
        JsonElement schema = paths.GetProperty(path).GetProperty(method)
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        return ResolveSchema(document, schema);
    }

    private static JsonElement ResolveSchema(JsonElement document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out JsonElement reference)) return schema;
        string name = reference.GetString()!.Split('/')[^1];
        return document.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static void AssertStringProperty(
        JsonElement schema,
        string propertyName,
        int? minimumLength,
        int maximumLength,
        string? pattern = null,
        string? descriptionContains = null)
    {
        JsonElement property = schema.GetProperty("properties").GetProperty(propertyName);
        property.GetProperty("maxLength").GetInt32().Should().Be(maximumLength);
        if (minimumLength is not null)
            property.GetProperty("minLength").GetInt32().Should().Be(minimumLength);
        if (pattern is not null)
            property.GetProperty("pattern").GetString().Should().Be(pattern);
        if (descriptionContains is not null)
            property.GetProperty("description").GetString().Should().Contain(descriptionContains);
    }

    private static void AssertParameter(
        JsonElement[] parameters,
        string name,
        string location,
        bool required,
        int? maximumLength = null,
        int? minimumLength = null,
        string? pattern = null,
        string? descriptionContains = null)
    {
        JsonElement parameter = parameters.Single(value => value.GetProperty("name").GetString() == name);
        parameter.GetProperty("in").GetString().Should().Be(location);
        (parameter.TryGetProperty("required", out JsonElement requiredProperty) && requiredProperty.GetBoolean())
            .Should().Be(required);
        if (maximumLength is not null)
            parameter.GetProperty("schema").GetProperty("maxLength").GetInt32().Should().Be(maximumLength);
        if (minimumLength is not null)
            parameter.GetProperty("schema").GetProperty("minLength").GetInt32().Should().Be(minimumLength);
        if (pattern is not null)
            parameter.GetProperty("schema").GetProperty("pattern").GetString().Should().Be(pattern);
        if (descriptionContains is not null)
            parameter.GetProperty("description").GetString().Should().Contain(descriptionContains);
    }

    private static string SuccessStatus(string operation) => operation switch
    {
        "provision" or "submit" or "import" => "201",
        "submit-and-activate" or "activate" or "rollback" or "import-and-activate" or "backup" or "purge" => "202",
        _ => "200",
    };

    private static IEnumerable<string> ErrorStatuses(string operation)
    {
        yield return "401"; yield return "403"; yield return "429"; yield return "500"; yield return "504";
        if (operation is not ("capabilities" or "host-capabilities" or "validate")) yield return "404";
        if (operation is "validate" or "submit" or "submit-and-activate" or "activate" or "rollback" or
            "compare" or "import" or "import-and-activate" or "backup" or "purge")
        { yield return "400"; yield return "413"; yield return "415"; }
        if (operation is "provision" or "submit" or "submit-and-activate" or "activate" or "rollback" or
            "import" or "import-and-activate")
        { yield return "409"; yield return "422"; yield return "503"; }
        if (operation == "export") yield return "410";
        if (operation is "backup" or "purge") yield return "503";
    }

    private static WebApplication Build(bool resourceAllowed, bool mapOpenApi = false)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", null);
        builder.Services.AddAuthorization(options =>
        {
            foreach (string capability in GatewayAdminCapabilities.All)
                options.AddPolicy(capability, policy => policy.RequireAuthenticatedUser());
            options.AddPolicy(GatewayAdminResourcePolicies.Namespace, policy => policy.RequireAssertion(_ => resourceAllowed));
            options.AddPolicy(GatewayAdminResourcePolicies.Target, policy => policy.RequireAssertion(_ => resourceAllowed));
            options.AddPolicy(GatewayAdminResourcePolicies.Administration, policy => policy.RequireAssertion(_ => resourceAllowed));
        });
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("gateway-management", limiter =>
        {
            limiter.PermitLimit = 16; limiter.QueueLimit = 0; limiter.Window = TimeSpan.FromSeconds(1);
        }));
        builder.Services.AddRequestTimeouts(options => options.AddPolicy("gateway-management", TimeSpan.FromSeconds(5)));
        builder.Services.AddSingleton<IGatewayAdminActorProjector, TestActorProjector>();
        builder.Services.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());
        builder.Services.AddManagementCore();
        builder.Services.AddAdminCore();
        if (mapOpenApi) builder.Services.AddOpenApi("hpd-gateway-v1");
        WebApplication app = builder.Build();
        app.UseRouting();
        app.Use((context, next) =>
        {
            bool data = context.Request.Headers.ContainsKey("x-test-listener");
            context.Features.Set<IHpdGatewayListenerFeature>(new TestListenerFeature(
                new("management"), data ? GatewayListenerRole.DataPlane : GatewayListenerRole.Management,
                data ? "gateway-data" : "gateway-admin-v1"));
            return next(context);
        });
        app.UseHpdGatewayListenerRoles();
        app.UseRequestTimeouts();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapGatewayAdminCore(new GatewayAdminApiOptions
        {
            AuthenticationScheme = "test",
            OpenApiSecurityScheme = "test",
            CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
                static capability => capability, static capability => capability, StringComparer.Ordinal),
        });
        if (mapOpenApi) app.MapOpenApi();
        return app;
    }

    private sealed record TestListenerFeature(
        ListenerId ListenerId, GatewayListenerRole Role, string EndpointSurfaceId) : IHpdGatewayListenerFeature;

    private sealed class TestActorProjector : IGatewayAdminActorProjector
    {
        public ValueTask<GatewayAdminRequestAttribution> ProjectAsync(
            HttpContext context, string capability, CancellationToken cancellationToken = default)
        {
            string correlation = context.Request.Headers["X-Correlation-ID"] is { Count: 1 } values &&
                values[0] is { Length: > 0 and <= 128 } value
                    ? value
                    : "correlation";
            return ValueTask.FromResult(new GatewayAdminRequestAttribution(
                "actor", "test", capability, correlation));
        }
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
