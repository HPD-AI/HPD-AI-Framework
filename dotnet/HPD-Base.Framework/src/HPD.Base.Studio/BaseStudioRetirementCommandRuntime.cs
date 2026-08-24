using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    private void AddRetirementCommands(BaseStudioModuleRegistration module, List<BaseStudioNamedTypeContract> types,
        List<BaseStudioEndpointContract> endpoints, List<BaseStudioMethodBinding> methods,
        List<BaseStudioProducerBinding> producers, BaseStudioNamedTypeContract error)
    {
        foreach (BaseStudioCommandRegistration command in module.Commands.Where(static value => value.CommandId.StartsWith("retirement.", StringComparison.Ordinal)))
        {
            BaseStudioNamedTypeContract text = types.Single(static value => value.TypeId == "base.studio.text");
            BaseStudioNamedTypeContract checksum = types.Single(static value => value.TypeId == "base.studio.sha256");
            BaseStudioNamedTypeContract optionalText = types.Single(static value => value.TypeId == "base.studio.optional-text");
            BaseStudioNamedTypeContract target = types.Single(value => value.TypeId == (command.CommandId == "retirement.consumer.remove"
                ? "base.studio.resource.lifecycleconsumer" : "base.studio.resource.retirementbarrier"));
            BaseStudioNamedTypeContract input = Type(command.InputNodeId, BaseStudioModuleRegistry.RetirementInputDescriptorForRuntime(command.CommandId));
            BaseStudioNamedTypeContract result = Type(command.ResultNodeId, BaseStudioModuleRegistry.RetirementResultDescriptorForRuntime);
            if (!BaseStudioSha256.FixedTimeEquals(input.NodeChecksum, command.InputNodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(result.NodeChecksum, command.ResultNodeChecksum))
                throw new InvalidOperationException("A retirement command differs from its graph-owned semantic nodes.");
            BaseStudioNamedTypeContract acknowledgement = Type("base.studio.command-acknowledgement", "{\"kind\":\"object\",\"properties\":[{\"name\":\"impactId\",\"wireName\":\"impactId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"purposeId\",\"wireName\":\"purposeId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
            BaseStudioNamedTypeContract acknowledgements = Type("base.studio.command-acknowledgements.one", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.command-acknowledgement\",\"minItems\":1,\"maxItems\":1}");
            BaseStudioNamedTypeContract previewRequest = Type(command.CommandId.ToLowerInvariant() + ".preview.request", Obj(P("commandId", text), P("input", input), P("pageId", text), P("responseAuthorityChecksum", checksum), P("target", target)));
            BaseStudioNamedTypeContract executeRequest = Type(command.CommandId.ToLowerInvariant() + ".execute.request", Obj(P("acknowledgements", acknowledgements), P("commandId", text), PN("freshAuthentication", optionalText), P("pageId", text), P("preview", result), P("requestIdentity", text), P("responseAuthorityChecksum", checksum), P("target", target)));
            types.AddRange([acknowledgement, acknowledgements, input, result, previewRequest, executeRequest]);
            string previewEndpoint = "base.studio.command." + command.CommandId + ".preview";
            string executeEndpoint = "base.studio.command." + command.CommandId + ".execute";
            string previewMethod = "base.studio.preview." + command.CommandId;
            string executeMethod = "base.studio.execute." + command.CommandId;
            endpoints.Add(CommandEndpoint(previewEndpoint, "/base/studio/commands/" + command.CommandId + "/preview", previewRequest, result));
            endpoints.Add(CommandEndpoint(executeEndpoint, "/base/studio/commands/" + command.CommandId + "/execute", executeRequest, result));
            methods.Add(BaseStudioMethodBinding.Create(previewMethod, BaseStudioMethodKind.Preview, "base", command.CommandId, previewEndpoint, previewRequest.TypeId, result.TypeId));
            methods.Add(BaseStudioMethodBinding.Create(executeMethod, BaseStudioMethodKind.Execute, "base", command.CommandId, executeEndpoint, executeRequest.TypeId, result.TypeId));
            var producer = new RetirementCommandProducer(_principals, _authorization, command.Grants, command.CommandId, _sessions, _timeProvider);
            producers.Add(new BaseStudioCommandPreviewProducerBinding(previewMethod, producer));
            producers.Add(new BaseStudioCommandExecuteProducerBinding(executeMethod, producer));
        }

        BaseStudioEndpointContract CommandEndpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result) =>
            BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route, BaseStudioEndpointAudience.ControlPlane,
                BaseStudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 1_048_576, 1_048_576, TimeSpan.FromMinutes(5));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string PN(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class RetirementCommandProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, string commandId, IBaseSessionFactory? sessions, TimeProvider timeProvider)
        : ProducerBase(principals, authorization, grants), IBaseStudioCommandProducer
    {
        private readonly ConcurrentDictionary<string, PreviewEvidence> _previews = new(StringComparer.Ordinal);

        public async ValueTask<BaseStudioCanonicalJson?> PreviewAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (sessions is null || !await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !TryPreview(invocation.Request, out BaseStudioResourceIdentity? target, out JsonElement semantic) || target is null ||
                target.ApplicationId != invocation.Bootstrap.ApplicationGraph.ApplicationId || !TargetMatches(target)) return null;
            (PrincipalContext Principal, OperationContext Operation)? context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false);
            BaseOwnedSubjectScopeEvidence? scope = await ScopeAsync(invocation, cancellationToken).ConfigureAwait(false);
            if (context is null || scope is null) return null;
            BaseSession session = Session(context.Value.Principal, scope);
            if (target is BaseStudioRetirementBarrierResource barrier)
            {
                BaseResult<BaseSubjectRetirementInspection> inspected = await session.SubjectRetirements.InspectAsync(new()
                {
                    ContractId = barrier.ContractId, ContractVersion = barrier.ContractVersion,
                    SubjectId = BaseSubjectId.Create(barrier.ProtectedSubjectIdentity, BaseSubjectIdKind.OrdinalString),
                    AuthorityEpoch = BaseSubjectAuthorityEpoch.Parse(barrier.AuthorityEpoch),
                    Incarnation = BaseSubjectIncarnation.Parse(barrier.Incarnation), IncludeTerminalSummary = false,
                    ScopeAuthority = new() { Mode = BaseSubjectScopeQueryMode.ExactScope, ExactScope = scope, InstalledAuthorityDigest = "base.studio.runtime" },
                    MaximumResultBytes = 1_048_576, DeadlineUtc = timeProvider.GetUtcNow().AddSeconds(30),
                }, cancellationToken).ConfigureAwait(false);
                if (!inspected.TryGetValue(out BaseSubjectRetirementInspection? inspection) || inspection?.CurrentBarrier is not { } current ||
                    current.Generation != Long(semantic, "expectedBarrierGeneration") ||
                    !StringComparer.Ordinal.Equals(current.BarrierChecksum, Text(semantic, "expectedBarrierChecksum"))) return null;
            }
            else if (target is BaseStudioLifecycleConsumerResource consumer &&
                (!StringComparer.Ordinal.Equals(consumer.ConsumerId, Text(semantic, "consumerId")) ||
                 consumer.Version != Long(semantic, "consumerVersion"))) return null;
            DateTimeOffset now = timeProvider.GetUtcNow();
            DateTimeOffset expires = now.AddMinutes(5);
            string checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(commandId + "\n" +
                Convert.ToHexString(target.AuthorityChecksum.ToArray()) + "\n" + semantic.GetRawText())));
            foreach ((string key, PreviewEvidence value) in _previews)
                if (value.ExpiresAtUtc <= now) _previews.TryRemove(key, out _);
            if (_previews.Count >= 1024 || !_previews.TryAdd(checksum, new(target.AuthorityChecksum, semantic.Clone(), expires))) return null;
            return Result("preview", checksum, null, Generation(semantic), expires);
        }

        public async ValueTask<BaseStudioCanonicalJson?> ExecuteAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (sessions is null || !await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !TryExecute(invocation.Request, out BaseStudioResourceIdentity? target, out string? requestIdentity, out string? previewChecksum) ||
                target is null || requestIdentity is null || previewChecksum is null || !TargetMatches(target) ||
                !_previews.TryGetValue(previewChecksum, out PreviewEvidence? preview) || preview.ExpiresAtUtc <= timeProvider.GetUtcNow() ||
                !BaseStudioSha256.FixedTimeEquals(preview.TargetChecksum, target.AuthorityChecksum))
                throw new BaseStudioCommandFailedBeforeInfluenceException();
            (PrincipalContext Principal, OperationContext Operation)? context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false);
            BaseOwnedSubjectScopeEvidence? scope = await ScopeAsync(invocation, cancellationToken).ConfigureAwait(false);
            if (context is null || scope is null) throw new BaseStudioCommandFailedBeforeInfluenceException();
            BaseSession session = Session(context.Value.Principal, scope);
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("base.studio", commandId, requestIdentity,
                BaseMutationRequestFingerprint.Create(SHA256.HashData(Encoding.UTF8.GetBytes(commandId + "\n" + previewChecksum + "\n" + preview.Semantic.GetRawText()))));
            (long generation, string receipt) result;
            try
            {
                result = await InvokeAsync(session, target, preview.Semantic, identity, cancellationToken).ConfigureAwait(false);
            }
            catch (BaseStudioCommandFailedBeforeInfluenceException)
            {
                // The hosting dispatcher restores its single-use execution and fresh-auth authority for this
                // exact request. Preserve the producer-owned preview as well so the exact retry can proceed.
                throw;
            }
            catch
            {
                // Influence is possible or indeterminate. Never permit this preview to authorize new work.
                _previews.TryRemove(previewChecksum, out _);
                throw;
            }
            _previews.TryRemove(previewChecksum, out _);
            return Result("execute", previewChecksum, result.receipt, result.generation, null);
        }

        private async ValueTask<(long Generation, string Receipt)> InvokeAsync(BaseSession session, BaseStudioResourceIdentity target,
            JsonElement input, BaseMutationRequestIdentity identity, CancellationToken token)
        {
            BaseResultMarker marker;
            if (target is BaseStudioRetirementBarrierResource barrier)
            {
                BaseSubjectId subject = BaseSubjectId.Create(barrier.ProtectedSubjectIdentity, BaseSubjectIdKind.OrdinalString);
                BaseSubjectAuthorityEpoch epoch = BaseSubjectAuthorityEpoch.Parse(barrier.AuthorityEpoch);
                BaseSubjectIncarnation incarnation = BaseSubjectIncarnation.Parse(barrier.Incarnation);
                marker = commandId switch
                {
                    "retirement.timeout" => Mark(await session.SubjectRetirements.ProcessTimeoutAsync(new() { ContractId=barrier.ContractId, ContractVersion=barrier.ContractVersion, SubjectId=subject, AuthorityEpoch=epoch, Incarnation=incarnation, ExpectedBarrierGeneration=Long(input,"expectedBarrierGeneration"), ExpectedBarrierChecksum=Text(input,"expectedBarrierChecksum"), Identity=identity }, token).ConfigureAwait(false)),
                    "retirement.override" => Mark(await session.SubjectRetirements.OverrideAsync(new() { ContractId=barrier.ContractId, ContractVersion=barrier.ContractVersion, SubjectId=subject, AuthorityEpoch=epoch, Incarnation=incarnation, ExpectedTombstoneSequence=Long(input,"expectedTombstoneSequence"), ExpectedBarrierGeneration=Long(input,"expectedBarrierGeneration"), ExpectedBarrierChecksum=Text(input,"expectedBarrierChecksum"), Intent=Text(input,"intent"), ChangeReference=Text(input,"changeReference"), Identity=identity }, token).ConfigureAwait(false)),
                    "retirement.purge" => Mark(await session.SubjectRetirements.PurgeAsync(new() { ContractId=barrier.ContractId, ContractVersion=barrier.ContractVersion, SubjectId=subject, AuthorityEpoch=epoch, Incarnation=incarnation, ExpectedTombstoneSequence=Long(input,"expectedTombstoneSequence"), ExpectedPrivateRevision=new RevisionToken(Text(input,"expectedPrivateRevision")), ExpectedBarrierGeneration=Long(input,"expectedBarrierGeneration"), ExpectedBarrierChecksum=Text(input,"expectedBarrierChecksum"), Identity=identity }, token).ConfigureAwait(false)),
                    _ => throw new BaseStudioCommandFailedBeforeInfluenceException(),
                };
            }
            else if (target is BaseStudioLifecycleConsumerResource consumer && commandId == "retirement.consumer.remove")
                marker = Mark(await session.SubjectRetirements.RemoveConsumerAsync(new() { ContractId=consumer.ContractId, ContractVersion=consumer.ContractVersion, ConsumerId=Text(input,"consumerId"), ConsumerVersion=checked((int)Long(input,"consumerVersion")), ExpectedConsumerChecksum=Text(input,"expectedConsumerChecksum"), ExpectedAcceptedSetChecksum=Text(input,"expectedAcceptedSetChecksum"), ExpectedGraphGeneration=Long(input,"expectedGraphGeneration"), Identity=identity }, token).ConfigureAwait(false));
            else throw new BaseStudioCommandFailedBeforeInfluenceException();
            return (marker.Generation, marker.Receipt);
        }

        private static BaseResultMarker Mark<T>(BaseResult<T> result)
        {
            if (!result.TryGetValue(out T? value) || value is null)
            {
                BaseFailure<T> failure = (BaseFailure<T>)result;
                if (failure.Status == OperationStatus.StoreError || failure.Error.Code.Contains("indeterminate", StringComparison.OrdinalIgnoreCase))
                    throw new BaseStudioCommandIndeterminateException();
                throw new BaseStudioCommandFailedBeforeInfluenceException();
            }
            long generation = value switch { BaseSubjectRetirementTimeoutResult x=>x.Generation, BaseSubjectRetirementOverrideResult x=>x.Generation, BaseSubjectRetirementConsumerRemovalResult x=>x.PublishedGraphGeneration, BaseSubjectFinalPurgeResult x=>x.RetiredPosition.Value, _=>0 };
            string receiptInput = value switch
            {
                BaseSubjectRetirementTimeoutResult x => $"timeout\n{(int)x.Outcome}\n{(int)x.State}\n{x.Generation}\n{x.BarrierChecksum}",
                BaseSubjectRetirementOverrideResult x => $"override\n{(int)x.Outcome}\n{x.Generation}\n{x.BarrierChecksum}",
                BaseSubjectRetirementConsumerRemovalResult x => $"consumer.remove\n{(int)x.Outcome}\n{x.PublishedGraphGeneration}\n{x.AcceptedConsumerSetChecksum}\n{x.ExaminedBarriers}\n{x.ResolvedBarriers}",
                BaseSubjectFinalPurgeResult x => $"purge\n{(int)x.Outcome}\n{x.RetiredSubjectSequence}\n{x.RetiredPosition.Value}\n{x.TerminalReceiptChecksum}",
                _ => throw new BaseStudioCommandIndeterminateException(),
            };
            return new(generation, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(receiptInput))));
        }

        private bool TargetMatches(BaseStudioResourceIdentity target) => commandId == "retirement.consumer.remove"
            ? target is BaseStudioLifecycleConsumerResource : target is BaseStudioRetirementBarrierResource;
        private BaseSession Session(PrincipalContext principal, BaseOwnedSubjectScopeEvidence scope) => sessions!.For(principal, options =>
        {
            options.Audience = HPDBaseEndpointAudience.ControlPlane; options.Mode = OperationMode.User;
            if (scope.Kind == BaseSubjectScopeKind.Tenant) options.TenantId = scope.Value;
            if (scope.Kind == BaseSubjectScopeKind.Project) options.ProjectId = scope.Value;
        });
        private static long Generation(JsonElement input) => input.TryGetProperty("expectedBarrierGeneration", out JsonElement barrier) ? Long(barrier) : Long(input.GetProperty("expectedGraphGeneration"));
        private static long Long(JsonElement input, string name) => Long(input.GetProperty(name));
        private static long Long(JsonElement value) => value.ValueKind == JsonValueKind.String
            ? long.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture) : value.GetInt64();
        private static string Text(JsonElement input, string name) => input.GetProperty(name).GetString() ?? throw new BaseStudioCommandFailedBeforeInfluenceException();
        private bool TryPreview(BaseStudioCanonicalJson request, out BaseStudioResourceIdentity? target, out JsonElement semantic)
        { target=null;semantic=default;try{using JsonDocument document=JsonDocument.Parse(request.ToArray());JsonElement root=document.RootElement;JsonElement input=root.GetProperty("input");if(root.GetProperty("commandId").GetString()!=commandId||input.GetProperty("mode").GetString()!="preview"||input.GetProperty("previewChecksum").ValueKind!=JsonValueKind.Null||!Decode(root.GetProperty("target"),out target)||!BaseStudioResourceRouteToken.TryDecode(input.GetProperty("resourceToken").GetString(),out BaseStudioResourceIdentity? token)||token is null||target is null||!BaseStudioSha256.FixedTimeEquals(token.AuthorityChecksum,target.AuthorityChecksum))return false;semantic=input.Clone();return true;}catch{return false;}}
        private bool TryExecute(BaseStudioCanonicalJson request,out BaseStudioResourceIdentity? target,out string? identity,out string? checksum)
        {target=null;identity=checksum=null;try{using JsonDocument document=JsonDocument.Parse(request.ToArray());JsonElement root=document.RootElement;if(root.GetProperty("commandId").GetString()!=commandId||!Decode(root.GetProperty("target"),out target))return false;identity=root.GetProperty("requestIdentity").GetString();checksum=root.GetProperty("preview").GetProperty("previewChecksum").GetString();return true;}catch{return false;}}
        private static bool Decode(JsonElement element,out BaseStudioResourceIdentity? resource){string value=Convert.ToBase64String(Encoding.UTF8.GetBytes(element.GetRawText())).TrimEnd('=').Replace('+','-').Replace('/','_');return BaseStudioResourceRouteToken.TryDecode(value,out resource);}
        private static BaseStudioCanonicalJson Result(string mode,string preview,string? receipt,long generation,DateTimeOffset? expiry){var buffer=new ArrayBufferWriter<byte>();using var writer=new Utf8JsonWriter(buffer);writer.WriteStartObject();writer.WritePropertyName("expiresAtUtc");if(expiry is{} time)writer.WriteStringValue(time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",CultureInfo.InvariantCulture));else writer.WriteNullValue();writer.WriteString("mode",mode);writer.WriteString("previewChecksum",preview);writer.WritePropertyName("receiptChecksum");if(receipt is null)writer.WriteNullValue();else writer.WriteStringValue(receipt);writer.WriteString("resultingGeneration",generation.ToString(CultureInfo.InvariantCulture));writer.WriteEndObject();writer.Flush();return BaseStudioCanonicalJson.Create(buffer.WrittenSpan,1_048_576);}
        private sealed record PreviewEvidence(BaseStudioSha256 TargetChecksum,JsonElement Semantic,DateTimeOffset ExpiresAtUtc);
        private sealed record BaseResultMarker(long Generation,string Receipt);
    }
}
