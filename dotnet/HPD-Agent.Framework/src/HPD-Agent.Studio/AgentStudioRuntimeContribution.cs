using System.Collections.Immutable;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Studio;

/// <summary>Builds Agent Studio's typed resource resolvers and Runtime endpoints.</summary>
public sealed class AgentStudioRuntimeContributionFactory(
    IServiceProvider services,
    IAgentStudioInspectionAuthority inspection) : IStudioModuleRuntimeContributionFactory
{
    private const int MaximumBytes = 1_048_576;

    public string ModuleId => "agents";

    public StudioModuleRuntimeContribution Create(StudioModuleRegistration module)
    {
        if (!StringComparer.Ordinal.Equals(module.Identity.ModuleId, ModuleId))
            throw new ArgumentException("The Agent Runtime factory cannot author another module.", nameof(module));
        StudioNamedTypeContract text = Type("agent.studio.text",
            "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":512,\"format\":\"nfc-text\"}");
        StudioNamedTypeContract checksum = Type("agent.studio.sha256",
            "{\"kind\":\"string\",\"minLength\":64,\"maxLength\":64,\"format\":\"sha256\"}");
        StudioNamedTypeContract tokenRequest = Type("agent.studio.resource-token-request", Obj(P("resourceToken", text)));
        StudioNamedTypeContract emptyMap = Type("agent.studio.empty-map",
            "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}");
        StudioNamedTypeContract routeParameters = Type("agent.studio.route-parameters", Obj(P("resource", text)));
        StudioNamedTypeContract route = Type("agent.studio.resolved-route",
            Obj(P("pageId", text), P("parameters", routeParameters), P("query", emptyMap)));
        StudioNamedTypeContract resolvedKind = Type("agent.studio.resolved-kind", "{\"kind\":\"literal\",\"value\":\"resolved\"}");
        StudioNamedTypeContract emptyLinks = Type("agent.studio.links.empty",
            "{\"kind\":\"array\",\"elementTypeId\":\"agent.studio.text\",\"minItems\":0,\"maxItems\":0}");
        StudioNamedTypeContract error = Type("agent.studio.safe-error",
            "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"safe-error-code\"}");
        var types = new List<StudioNamedTypeContract>
            { checksum, emptyLinks, emptyMap, error, resolvedKind, route, routeParameters, text, tokenRequest };
        var endpoints = new List<StudioEndpointContract>();
        var methods = new List<StudioMethodBinding>();
        var producers = new List<StudioProducerBinding>();

        Add(StudioResourceKind.Application, "agent.studio.resolve.application", "agent.studio.resource.application",
            "application", "agent.catalog", []);
        Add(StudioResourceKind.Agent, "agent.studio.resolve.agent", "agent.studio.resource.agent",
            "agent", "agent.overview", [("agentId", text)]);
        Add(StudioResourceKind.AgentSession, "agent.studio.resolve.session", "agent.studio.resource.session",
            "agentSession", "session.detail", [("sessionId", text)]);
        Add(StudioResourceKind.AgentThread, "agent.studio.resolve.thread", "agent.studio.resource.thread",
            "agentThread", "thread.detail", [("sessionId", text), ("threadId", text)]);
        Add(StudioResourceKind.AgentExecution, "agent.studio.resolve.execution", "agent.studio.resource.execution",
            "agentExecution", "execution.detail",
            [("sessionId", text), ("threadId", text), ("threadExecutionId", text)]);
        Add(StudioResourceKind.ToolHarness, "agent.studio.resolve.harness", "agent.studio.resource.harness",
            "toolHarness", "harness.detail", [("harnessId", text)]);
        Add(StudioResourceKind.AiFunction, "agent.studio.resolve.function", "agent.studio.resource.function",
            "aiFunction", "aiFunction.detail", [("harnessId", text), ("functionId", text)]);
        Add(StudioResourceKind.AiFunctionAction, "agent.studio.resolve.action", "agent.studio.resource.action",
            "aiFunctionAction", "aiFunction.action.detail",
            [("harnessId", text), ("functionId", text), ("actionId", text)]);
        Add(StudioResourceKind.AgentTrace, "agent.studio.resolve.trace", "agent.studio.resource.trace",
            "agentTrace", "trace.detail", [("traceId", text)]);
        AddConfigurationCommand();
        AddCreationCommand();
        AddExperimentCommands();
        AddLifecycleCommands();
        AddThreadActivitySubscription();
        AddEventSubscription();

        return StudioModuleRuntimeContribution.Create(module,
            types.OrderBy(static value => value.TypeId, StringComparer.Ordinal),
            endpoints.OrderBy(static value => (value.EndpointId, value.Version)),
            methods.OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal),
            producers.OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal));

        void AddConfigurationCommand()
        {
            StudioCommandRegistration command = module.Commands.Single(value =>
                StringComparer.Ordinal.Equals(value.CommandId, "agent.configuration.update"));
            StudioNamedTypeContract optionalText = Type("agent.studio.optional-text",
                "{\"kind\":\"string\",\"minLength\":0,\"maxLength\":4096,\"format\":\"plain\"}");
            StudioNamedTypeContract optionalChecksum = Type("agent.studio.optional-sha256",
                "{\"kind\":\"string\",\"minLength\":0,\"maxLength\":64,\"format\":\"optional-sha256\"}");
            StudioNamedTypeContract configurationMode = Type("agent.studio.configuration-command-mode",
                "{\"kind\":\"enum\",\"values\":[\"execute\",\"preview\"]}");
            StudioNamedTypeContract changeKind = Type("agent.studio.configuration-change-kind",
                "{\"kind\":\"enum\",\"values\":[\"added\",\"changed\",\"removed\"]}");
            StudioNamedTypeContract diffItem = Type("agent.studio.configuration-diff-item", Obj(
                P("path", text), P("change", changeKind), PN("before", optionalText), PN("after", optionalText)));
            StudioNamedTypeContract diffs = Type("agent.studio.configuration-diffs",
                "{\"kind\":\"array\",\"elementTypeId\":\"agent.studio.configuration-diff-item\",\"minItems\":0,\"maxItems\":256}");
            StudioNamedTypeContract changeCount = Type("agent.studio.configuration-change-count",
                "{\"kind\":\"integer\",\"wire\":\"number\",\"minimum\":\"0\",\"maximum\":\"256\"}");
            StudioNamedTypeContract acknowledgement = Type("agent.studio.command-acknowledgement", Obj(
                P("purposeId", text), P("impactId", text)));
            StudioNamedTypeContract acknowledgements = Type("agent.studio.command-acknowledgements.one",
                "{\"kind\":\"array\",\"elementTypeId\":\"agent.studio.command-acknowledgement\",\"minItems\":1,\"maxItems\":1}");
            StudioNamedTypeContract executeAcknowledgement = Type("agent.studio.execute-acknowledgement", Obj(
                P("purposeId", text), P("impactId", text), P("previewChecksum", checksum)));
            StudioNamedTypeContract executeAcknowledgements = Type("agent.studio.execute-acknowledgements.one",
                "{\"kind\":\"array\",\"elementTypeId\":\"agent.studio.execute-acknowledgement\",\"minItems\":1,\"maxItems\":1}");
            StudioNamedTypeContract configurationJson = Type("agent.studio.configuration-json",
                "{\"kind\":\"string\",\"minLength\":2,\"maxLength\":1048576,\"format\":\"plain\"}");
            StudioNamedTypeContract configurationInput = AgentStudioModuleRegistry.ConfigurationUpdateInputType;
            StudioNamedTypeContract result = AgentStudioModuleRegistry.ConfigurationUpdateResultType;
            StudioNamedTypeContract agent = types.Single(value => value.TypeId == "agent.studio.resource.agent");
            StudioNamedTypeContract previewRequest = Type("agent.configuration.update.preview.request", Obj(
                P("commandId", text), P("pageId", text), P("target", agent), P("input", configurationInput),
                P("responseAuthorityChecksum", checksum)));
            StudioNamedTypeContract executeRequest = Type("agent.configuration.update.execute.request", Obj(
                P("commandId", text), P("pageId", text), P("target", agent), P("preview", result),
                P("requestIdentity", text), P("acknowledgements", executeAcknowledgements),
                PN("freshAuthentication", optionalText), P("responseAuthorityChecksum", checksum)));
            types.AddRange([
                acknowledgement, acknowledgements, changeCount, changeKind, configurationJson,
                diffItem, diffs, executeAcknowledgement, executeAcknowledgements, optionalChecksum,
                optionalText, configurationMode,
            ]);
            types.Add(configurationInput);
            types.Add(result);
            types.Add(previewRequest);
            types.Add(executeRequest);

            const string previewMethod = "agent.studio.command.agent.configuration.update.preview";
            const string executeMethod = "agent.studio.command.agent.configuration.update.execute";
            const string previewEndpoint = "agent.studio.command.agent.configuration.update.preview";
            const string executeEndpoint = "agent.studio.command.agent.configuration.update.execute";
            endpoints.Add(StudioEndpointContract.Create(previewEndpoint, 1, StudioTransportMethod.Post,
                "/agents/studio/commands/agent.configuration.update/preview", StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, previewRequest.TypeId, previewRequest.NodeChecksum,
                result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                1_048_576, MaximumBytes, TimeSpan.FromSeconds(30)));
            endpoints.Add(StudioEndpointContract.Create(executeEndpoint, 1, StudioTransportMethod.Post,
                "/agents/studio/commands/agent.configuration.update/execute", StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, executeRequest.TypeId, executeRequest.NodeChecksum,
                result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                1_048_576, MaximumBytes, TimeSpan.FromSeconds(30)));
            methods.Add(StudioMethodBinding.Create(previewMethod, StudioMethodKind.Preview, "agents",
                command.CommandId, previewEndpoint, previewRequest.TypeId, result.TypeId));
            methods.Add(StudioMethodBinding.Create(executeMethod, StudioMethodKind.Execute, "agents",
                command.CommandId, executeEndpoint, executeRequest.TypeId, result.TypeId));
            var producer = new ConfigurationUpdateProducer(services);
            producers.Add(new StudioCommandPreviewProducerBinding(previewMethod, producer));
            producers.Add(new StudioCommandExecuteProducerBinding(executeMethod, producer));
        }

        void AddCreationCommand()
        {
            StudioCommandRegistration command = module.Commands.Single(value =>
                StringComparer.Ordinal.Equals(value.CommandId, AgentStudioCreationContracts.CreateAgent));
            StudioNamedTypeContract executeAcknowledgements = types.Single(value =>
                value.TypeId == "agent.studio.execute-acknowledgements.one");
            StudioNamedTypeContract agentId = Type("agent.studio.agent-id",
                "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":128,\"format\":\"plain\"}");
            StudioNamedTypeContract application = types.Single(value =>
                value.TypeId == "agent.studio.resource.application");
            StudioNamedTypeContract runtimeInput = RuntimeType(AgentStudioCreationContracts.Input);
            StudioNamedTypeContract runtimeResult = RuntimeType(AgentStudioCreationContracts.Result);
            types.AddRange([agentId, runtimeInput, runtimeResult]);
            StudioNamedTypeContract previewRequest = Type("agent.create.preview.request", Obj(
                P("commandId", text), P("input", runtimeInput), P("pageId", text), P("target", application),
                P("responseAuthorityChecksum", checksum)));
            StudioNamedTypeContract executeRequest = Type("agent.create.execute.request", Obj(
                P("acknowledgements", executeAcknowledgements), P("commandId", text),
                PN("freshAuthentication", types.Single(value => value.TypeId == "agent.studio.optional-text")),
                P("pageId", text), P("preview", runtimeResult), P("requestIdentity", text),
                P("responseAuthorityChecksum", checksum), P("target", application)));
            types.Add(previewRequest);
            types.Add(executeRequest);
            const string previewMethod = "agent.studio.command.agent.create.preview";
            const string executeMethod = "agent.studio.command.agent.create.execute";
            endpoints.Add(StudioEndpointContract.Create(previewMethod, 1, StudioTransportMethod.Post,
                "/agents/studio/commands/agent.create/preview", StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, previewRequest.TypeId, previewRequest.NodeChecksum,
                runtimeResult.TypeId, runtimeResult.NodeChecksum, error.TypeId, error.NodeChecksum,
                65_536, MaximumBytes, TimeSpan.FromSeconds(30)));
            endpoints.Add(StudioEndpointContract.Create(executeMethod, 1, StudioTransportMethod.Post,
                "/agents/studio/commands/agent.create/execute", StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, executeRequest.TypeId, executeRequest.NodeChecksum,
                runtimeResult.TypeId, runtimeResult.NodeChecksum, error.TypeId, error.NodeChecksum,
                1_048_576, MaximumBytes, TimeSpan.FromSeconds(30)));
            methods.Add(StudioMethodBinding.Create(previewMethod, StudioMethodKind.Preview, "agents",
                command.CommandId, previewMethod, previewRequest.TypeId, runtimeResult.TypeId));
            methods.Add(StudioMethodBinding.Create(executeMethod, StudioMethodKind.Execute, "agents",
                command.CommandId, executeMethod, executeRequest.TypeId, runtimeResult.TypeId));
            var producer = new AgentStudioCreationCommandProducer(services);
            producers.Add(new StudioCommandPreviewProducerBinding(previewMethod, producer));
            producers.Add(new StudioCommandExecuteProducerBinding(executeMethod, producer));
        }

        void AddExperimentCommands()
        {
            StudioNamedTypeContract optionalText = types.Single(value => value.TypeId == "agent.studio.optional-text");
            StudioNamedTypeContract optionalChecksum = types.Single(value => value.TypeId == "agent.studio.optional-sha256");
            StudioNamedTypeContract boolean = Type("agent.studio.boolean",
                "{\"kind\":\"boolean\"}");
            StudioNamedTypeContract messageText = Type("agent.studio.message-text",
                "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":32768,\"format\":\"plain\"}");
            StudioNamedTypeContract threadName = Type("agent.studio.thread-name",
                "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"nfc-text\"}");
            StudioNamedTypeContract answersJson = Type("agent.studio.answers-json",
                "{\"kind\":\"string\",\"minLength\":2,\"maxLength\":65536,\"format\":\"plain\"}");
            StudioNamedTypeContract questionOutcome = Type("agent.studio.question-outcome",
                "{\"kind\":\"enum\",\"values\":[\"answered\",\"discuss\",\"dismissed\"]}");
            StudioNamedTypeContract extensionCount = Type("agent.studio.extension-count",
                "{\"kind\":\"integer\",\"minimum\":\"0\",\"maximum\":\"1000\",\"wire\":\"number\"}");
            StudioNamedTypeContract clientToolOutcome = Type("agent.studio.client-tool-outcome",
                "{\"kind\":\"enum\",\"values\":[\"acceptedBackground\",\"completed\",\"failed\",\"rejected\"]}");
            StudioNamedTypeContract experimentMode = Type("agent.studio.experiment-mode",
                "{\"kind\":\"enum\",\"values\":[\"execute\",\"preview\"]}");
            StudioNamedTypeContract emptyAcknowledgements = Type("agent.studio.experiment-empty-acknowledgements",
                "{\"kind\":\"array\",\"elementTypeId\":\"agent.studio.text\",\"minItems\":0,\"maxItems\":0}");
            types.AddRange([answersJson, boolean, clientToolOutcome, emptyAcknowledgements,
                experimentMode, extensionCount, messageText, questionOutcome, threadName]);
            StudioNamedTypeContract agent = types.Single(value => value.TypeId == "agent.studio.resource.agent");

            string[] experimentCommandIds =
            [
                AgentStudioExperimentContracts.CancelExecution,
                AgentStudioExperimentContracts.ClientToolRespond,
                AgentStudioExperimentContracts.ContinuationRespond,
                AgentStudioExperimentContracts.CreateSession,
                AgentStudioExperimentContracts.CreateThread,
                AgentStudioExperimentContracts.ForkThread,
                AgentStudioExperimentContracts.PermissionRespond,
                AgentStudioExperimentContracts.QuestionRespond,
                AgentStudioExperimentContracts.SteerInput,
                AgentStudioExperimentContracts.SubmitInput,
            ];
            foreach (StudioCommandRegistration command in module.Commands.Where(value =>
                         experimentCommandIds.Contains(value.CommandId, StringComparer.Ordinal)))
            {
                StudioNamedTypeContract inputContract = AgentStudioExperimentContracts.Input(command.CommandId);
                StudioNamedTypeContract resultContract = AgentStudioExperimentContracts.Result(command.CommandId);
                StudioNamedTypeContract input = RuntimeType(inputContract);
                StudioNamedTypeContract result = RuntimeType(resultContract);
                types.Add(input);
                types.Add(result);
                StudioNamedTypeContract previewRequest = Type(command.CommandId + ".preview.request", Obj(
                    P("commandId", text), P("input", input), P("pageId", text), P("target", agent),
                    P("responseAuthorityChecksum", checksum)));
                StudioNamedTypeContract executeRequest = Type(command.CommandId + ".execute.request", Obj(
                    P("acknowledgements", emptyAcknowledgements), P("commandId", text),
                    PN("freshAuthentication", optionalText), P("pageId", text), P("preview", result),
                    P("requestIdentity", text), P("responseAuthorityChecksum", checksum), P("target", agent)));
                types.Add(previewRequest);
                types.Add(executeRequest);
                string previewMethod = "agent.studio.command." + command.CommandId + ".preview";
                string executeMethod = "agent.studio.command." + command.CommandId + ".execute";
                string previewEndpoint = previewMethod;
                string executeEndpoint = executeMethod;
                endpoints.Add(StudioEndpointContract.Create(previewEndpoint, 1, StudioTransportMethod.Post,
                    "/agents/studio/commands/" + command.CommandId + "/preview", StudioEndpointAudience.ControlPlane,
                    StudioTransportKind.SameOriginHttp, previewRequest.TypeId, previewRequest.NodeChecksum,
                    result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                    65_536, MaximumBytes, TimeSpan.FromSeconds(30)));
                endpoints.Add(StudioEndpointContract.Create(executeEndpoint, 1, StudioTransportMethod.Post,
                    "/agents/studio/commands/" + command.CommandId + "/execute", StudioEndpointAudience.ControlPlane,
                    StudioTransportKind.SameOriginHttp, executeRequest.TypeId, executeRequest.NodeChecksum,
                    result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                    1_048_576, MaximumBytes, TimeSpan.FromSeconds(30)));
                methods.Add(StudioMethodBinding.Create(previewMethod, StudioMethodKind.Preview, "agents",
                    command.CommandId, previewEndpoint, previewRequest.TypeId, result.TypeId));
                methods.Add(StudioMethodBinding.Create(executeMethod, StudioMethodKind.Execute, "agents",
                    command.CommandId, executeEndpoint, executeRequest.TypeId, result.TypeId));
                var producer = new AgentStudioExperimentCommandProducer(services, command.CommandId);
                producers.Add(new StudioCommandPreviewProducerBinding(previewMethod, producer));
                producers.Add(new StudioCommandExecuteProducerBinding(executeMethod, producer));
            }
        }

        void AddLifecycleCommands()
        {
            StudioNamedTypeContract optionalText = types.Single(value => value.TypeId == "agent.studio.optional-text");
            StudioNamedTypeContract executeAcknowledgements = types.Single(value =>
                value.TypeId == "agent.studio.execute-acknowledgements.one");
            StudioNamedTypeContract session = types.Single(value =>
                value.TypeId == "agent.studio.resource.agent-session");
            StudioNamedTypeContract thread = types.Single(value =>
                value.TypeId == "agent.studio.resource.agent-thread");

            foreach ((string commandId, StudioNamedTypeContract target) in new[]
                     {
                         (AgentStudioExperimentContracts.DeleteSession, session),
                         (AgentStudioExperimentContracts.DeleteThread, thread),
                     })
            {
                StudioNamedTypeContract runtimeInput = RuntimeType(
                    AgentStudioExperimentContracts.Input(commandId));
                StudioNamedTypeContract runtimeResult = RuntimeType(
                    AgentStudioExperimentContracts.Result(commandId));
                if (!types.Any(value => StringComparer.Ordinal.Equals(value.TypeId, runtimeInput.TypeId)))
                    types.Add(runtimeInput);
                if (!types.Any(value => StringComparer.Ordinal.Equals(value.TypeId, runtimeResult.TypeId)))
                    types.Add(runtimeResult);
                StudioNamedTypeContract previewRequest = Type(commandId + ".preview.request", Obj(
                    P("commandId", text), P("input", runtimeInput), P("pageId", text), P("target", target),
                    P("responseAuthorityChecksum", checksum)));
                StudioNamedTypeContract executeRequest = Type(commandId + ".execute.request", Obj(
                    P("acknowledgements", executeAcknowledgements), P("commandId", text),
                    PN("freshAuthentication", optionalText), P("pageId", text), P("preview", runtimeResult),
                    P("requestIdentity", text), P("responseAuthorityChecksum", checksum), P("target", target)));
                types.Add(previewRequest);
                types.Add(executeRequest);
                string previewMethod = "agent.studio.command." + commandId + ".preview";
                string executeMethod = "agent.studio.command." + commandId + ".execute";
                string previewEndpoint = previewMethod;
                string executeEndpoint = executeMethod;
                endpoints.Add(StudioEndpointContract.Create(previewEndpoint, 1, StudioTransportMethod.Post,
                    "/agents/studio/commands/" + commandId + "/preview", StudioEndpointAudience.ControlPlane,
                    StudioTransportKind.SameOriginHttp, previewRequest.TypeId, previewRequest.NodeChecksum,
                    runtimeResult.TypeId, runtimeResult.NodeChecksum, error.TypeId, error.NodeChecksum,
                    65_536, MaximumBytes, TimeSpan.FromSeconds(30)));
                endpoints.Add(StudioEndpointContract.Create(executeEndpoint, 1, StudioTransportMethod.Post,
                    "/agents/studio/commands/" + commandId + "/execute", StudioEndpointAudience.ControlPlane,
                    StudioTransportKind.SameOriginHttp, executeRequest.TypeId, executeRequest.NodeChecksum,
                    runtimeResult.TypeId, runtimeResult.NodeChecksum, error.TypeId, error.NodeChecksum,
                    1_048_576, MaximumBytes, TimeSpan.FromSeconds(30)));
                methods.Add(StudioMethodBinding.Create(previewMethod, StudioMethodKind.Preview, "agents",
                    commandId, previewEndpoint, previewRequest.TypeId, runtimeResult.TypeId));
                methods.Add(StudioMethodBinding.Create(executeMethod, StudioMethodKind.Execute, "agents",
                    commandId, executeEndpoint, executeRequest.TypeId, runtimeResult.TypeId));
                var producer = new AgentStudioLifecycleCommandProducer(services, commandId);
                producers.Add(new StudioCommandPreviewProducerBinding(previewMethod, producer));
                producers.Add(new StudioCommandExecuteProducerBinding(executeMethod, producer));
            }
        }

        void AddThreadActivitySubscription()
        {
            StudioNamedTypeContract agent = types.Single(value => value.TypeId == "agent.studio.resource.agent");
            StudioNamedTypeContract position = Type("agent.studio.journal-position",
                "{\"kind\":\"integer\",\"minimum\":\"0\",\"maximum\":\"9007199254740991\",\"wire\":\"number\"}");
            StudioNamedTypeContract invalidationKind = Type("agent.studio.invalidation-kind",
                "{\"kind\":\"literal\",\"value\":\"invalidation\"}");
            StudioNamedTypeContract request = Type("agent.thread.activity.subscribe.request", Obj(
                P("sessionId", text), P("target", agent), P("threadId", text)));
            StudioNamedTypeContract result = Type("agent.thread.activity.invalidation", Obj(
                P("generation", position), P("kind", invalidationKind), P("sequenceNumber", position),
                P("threadId", text)));
            types.AddRange([invalidationKind, position, request, result]);
            const string endpointId = "agent.studio.thread.activity.subscribe";
            const string methodId = "agent.studio.subscribe.thread.activity";
            endpoints.Add(StudioEndpointContract.Create(endpointId, 1, StudioTransportMethod.Get,
                "/agents/studio/subscriptions/thread-activity", StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum,
                result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                16_384, 16_384, TimeSpan.FromMinutes(2)));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.InvalidationSubscribe,
                "agents", "agent.workspace", endpointId, request.TypeId, result.TypeId));
            producers.Add(new StudioSubscriptionProducerBinding(methodId,
                new AgentStudioThreadActivitySubscriptionProducer(services)));
        }

        void AddEventSubscription()
        {
            StudioNamedTypeContract position = Type("agent.studio.event-position",
                "{\"kind\":\"integer\",\"minimum\":\"0\",\"maximum\":\"9007199254740991\",\"wire\":\"number\"}");
            StudioNamedTypeContract eventAttributes = Type("agent.events.live.attributes", Obj(
                PN("request.source", text), PN("thread.execution.id", text)));
            StudioNamedTypeContract eventKind = Type("agent.events.live.kind",
                "{\"kind\":\"literal\",\"value\":\"event\"}");
            StudioNamedTypeContract request = Type("agent.events.subscribe.request", Obj());
            StudioNamedTypeContract boolean = types.Single(value => value.TypeId == "agent.studio.boolean");
            StudioNamedTypeContract result = Type("agent.events.live.event", Obj(
                P("agentId", text), P("attributes", eventAttributes), P("channel", text),
                P("cursor", text), P("eventId", text), P("eventKind", text), P("isError", boolean),
                P("kind", eventKind), P("level", text), P("occurredAt", text),
                PN("parentSpanId", text), P("sequenceNumber", position), P("sessionId", text),
                P("source", text), PN("spanId", text), P("threadId", text), P("traceId", text),
                P("type", text)));
            types.AddRange([eventAttributes, eventKind, position, request, result]);
            const string endpointId = "agent.studio.events.subscribe";
            const string methodId = "agent.studio.subscribe.events";
            endpoints.Add(StudioEndpointContract.Create(endpointId, 1, StudioTransportMethod.Get,
                "/agents/studio/subscriptions/events", StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum,
                result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                16_384, 16_384, TimeSpan.FromMinutes(2)));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.InvalidationSubscribe,
                "agents", "observability.events", endpointId, request.TypeId, result.TypeId));
            producers.Add(new StudioSubscriptionProducerBinding(methodId,
                new AgentStudioEventSubscriptionProducer(services)));
        }

        void Add(StudioResourceKind kind, string methodId, string endpointId, string name, string page,
            (string Name, StudioNamedTypeContract Type)[] identityMembers)
        {
            StudioResourceRegistration registered = module.Resources.Single(value => value.Kind == kind);
            if (!StringComparer.Ordinal.Equals(registered.ResolverId, methodId) ||
                !registered.EndpointIds.SequenceEqual([endpointId]))
                throw new InvalidOperationException("Agent resource registration differs from its Runtime resolver.");
            string typeName = Kebab(name);
            StudioNamedTypeContract kindType = Type("agent.studio." + typeName + "-kind",
                $"{{\"kind\":\"literal\",\"value\":\"{name}\"}}");
            var resourceProperties = new List<string>
            {
                P("applicationId", text),
                P("authorityChecksum", checksum),
                P("kind", kindType),
            };
            resourceProperties.AddRange(identityMembers.Select(value => P(value.Name, value.Type)));
            StudioNamedTypeContract resource = Type("agent.studio.resource." + typeName, Obj(resourceProperties.ToArray()));
            StudioNamedTypeContract result = Type("agent.studio.resolved." + typeName,
                Obj(P("kind", resolvedKind), P("links", emptyLinks), P("resource", resource), P("route", route)));
            types.Add(kindType);
            types.Add(resource);
            types.Add(result);
            endpoints.Add(StudioEndpointContract.Create(endpointId, 1, StudioTransportMethod.Post,
                "/agents/studio/resources/" + typeName, StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, tokenRequest.TypeId, tokenRequest.NodeChecksum,
                result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum, 16_384, MaximumBytes,
                TimeSpan.FromSeconds(10)));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Resolve, "agents", methodId,
                endpointId, tokenRequest.TypeId, result.TypeId));
            producers.Add(new StudioResourceProducerBinding(methodId,
                new Resolver(services, inspection, kind, page)));
        }
    }

    private static StudioNamedTypeContract Type(string id, string descriptor) =>
        StudioNamedTypeContract.Create(id, System.Text.Encoding.UTF8.GetBytes(descriptor));

    private static StudioNamedTypeContract RuntimeType(StudioNamedTypeContract contract) =>
        StudioNamedTypeContract.Create(contract.TypeId, contract.GetCanonicalDescriptor());

    private static string P(string name, StudioNamedTypeContract type) =>
        $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";

    private static string PN(string name, StudioNamedTypeContract type) =>
        $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}}";

    private static string Kebab(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 4);
        foreach (char character in value)
        {
            if (character is >= 'A' and <= 'Z')
            {
                if (result.Length > 0) result.Append('-');
                result.Append(char.ToLowerInvariant(character));
            }
            else result.Append(character);
        }
        return result.ToString();
    }

    private static string Obj(params string[] properties) =>
        $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties.Order(StringComparer.Ordinal))}],\"additionalProperties\":false}}";

    private sealed class Resolver(
        IServiceProvider services,
        IAgentStudioInspectionAuthority inspection,
        StudioResourceKind kind,
        string page) : IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(
            StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            if (!(await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
                    .AuthorizeAsync(invocation.Bootstrap.HttpContext.User, null, "base.studio.resource.inspect")
                    .ConfigureAwait(false)).Succeeded)
                return null;
            if (!TryToken(invocation.Request, out StudioResourceIdentity? identity) ||
                identity is null || identity.Kind != kind ||
                !StringComparer.Ordinal.Equals(identity.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId) ||
                !await inspection.ExistsAsync(identity, cancellationToken).ConfigureAwait(false))
                return null;
            return StudioResolvedResourceJson.Encode(identity,
                StudioResolvedRoute.Create(page,
                    [new KeyValuePair<string, string>("resource", StudioResourceRouteToken.Encode(identity))]),
                [], MaximumBytes);
        }

        private static bool TryToken(StudioCanonicalJson request, out StudioResourceIdentity? resource)
        {
            resource = null;
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.EnumerateObject().Select(static value => value.Name).SequenceEqual(["resourceToken"]) &&
                StudioResourceRouteToken.TryDecode(root.GetProperty("resourceToken").GetString(), out resource);
        }
    }
}
