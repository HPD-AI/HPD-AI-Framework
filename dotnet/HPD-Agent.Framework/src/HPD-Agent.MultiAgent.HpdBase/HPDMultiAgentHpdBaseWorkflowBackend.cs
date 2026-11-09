using System.Security.Cryptography;
using System.Text;
using HPD.Agent.MultiAgent.AspNetCore;
using HPD.Base;
using HPD.Base.AspNetCore;
using HPD.Graph.Base;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.MultiAgent.HpdBase;

internal sealed class HPDMultiAgentHpdBaseWorkflowBackend : IHPDMultiAgentWorkflowBackend
{
    private readonly IReadOnlyDictionary<string, BaseGraphActivationDefinition> _graphs;

    public HPDMultiAgentHpdBaseWorkflowBackend(IEnumerable<BaseGraphActivationDefinition> graphs)
    {
        BaseGraphActivationDefinition[] definitions = graphs
            .OrderBy(static graph => graph.GraphId, StringComparer.Ordinal)
            .ToArray();
        if (definitions.Length == 0 ||
            definitions.Select(static graph => graph.GraphId).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
        {
            throw new InvalidOperationException("HPD Base multi-agent graphs must have unique identifiers.");
        }

        _graphs = definitions.ToDictionary(static graph => graph.GraphId, StringComparer.Ordinal);
        Workflows = definitions.Select(static graph => new HPDMultiAgentWorkflow
        {
            WorkflowId = graph.GraphId,
            WorkflowVersion = graph.GraphVersion,
            DefinitionVersion = graph.Registration.Definition.Version,
            Checksum = Convert.ToHexStringLower(graph.GraphChecksum.Span),
        }).ToArray();
    }

    public IReadOnlyList<HPDMultiAgentWorkflow> Workflows { get; }

    public async ValueTask<HPDMultiAgentRunSubmission> EnqueueAsync(
        HPDMultiAgentRunCommand command,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!_graphs.TryGetValue(command.WorkflowId, out BaseGraphActivationDefinition? graph))
            return HPDMultiAgentRunSubmission.Failed(StatusCodes.Status404NotFound, "multi-agent.workflow.notFound");

        using var inputBuffer = new MemoryStream();
        using (var inputWriter = new System.Text.Json.Utf8JsonWriter(inputBuffer))
            command.Input.WriteTo(inputWriter);
        byte[] source = inputBuffer.ToArray();
        byte[] canonical;
        try
        {
            canonical = BaseGraphActivationRegistration.CanonicalJson(source);
        }
        catch (System.Text.Json.JsonException)
        {
            return HPDMultiAgentRunSubmission.Failed(StatusCodes.Status400BadRequest, "base.activation.invalid");
        }

        string executionId = string.IsNullOrWhiteSpace(command.ExecutionId)
            ? $"run:{Convert.ToHexStringLower(SHA256.HashData(canonical))}:{command.IdempotencyKey}"
            : command.ExecutionId;
        BaseGraphActivationInput input;
        BaseMutationRequestIdentity identity;
        try
        {
            input = graph.CreateInput(executionId, canonical);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("hpd.multiAgent.activation.v1\0"u8);
            hash.AppendData(graph.GraphChecksum.Span);
            hash.AppendData(SHA256.HashData(canonical));
            if (command.DueAt is DateTimeOffset dueAt)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(
                    dueAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            identity = BaseMutationRequestIdentity.Create(
                $"multi-agent:{graph.GraphId}",
                "enqueue",
                command.IdempotencyKey,
                BaseMutationRequestFingerprint.Create(hash.GetHashAndReset()));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return HPDMultiAgentRunSubmission.Failed(StatusCodes.Status400BadRequest, "base.activation.invalid");
        }

        PrincipalContext principal = await context.RequestServices
            .GetRequiredService<IBaseHttpPrincipalContextFactory>()
            .CreateAsync(context, cancellationToken).ConfigureAwait(false);
        BaseSession session = context.RequestServices.GetRequiredService<IBaseSessionFactory>().For(principal);
        OperationResult<BaseActivationEnqueueResult> result = await session.Activations
            .Get(graph.Registration.Identity)
            .EnqueueAsync(input, identity, new BaseActivationEnqueueOptions
            {
                DueAt = command.DueAt,
            }, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null)
        {
            return HPDMultiAgentRunSubmission.Failed(
                StatusCode(result.Status),
                result.Error?.Code ?? "base.activation.storeError");
        }

        return HPDMultiAgentRunSubmission.Accepted(new HPDMultiAgentRunAdmission
        {
            ExecutionId = executionId,
            ActivationId = result.Value.ActivationId,
            State = result.Value.State.ToString(),
            Disposition = result.Value.Disposition.ToString(),
        });
    }

    private static int StatusCode(OperationStatus status) => status switch
    {
        OperationStatus.ValidationFailed => StatusCodes.Status400BadRequest,
        OperationStatus.PolicyDenied => StatusCodes.Status403Forbidden,
        OperationStatus.NotFound => StatusCodes.Status404NotFound,
        OperationStatus.Conflict => StatusCodes.Status409Conflict,
        OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => StatusCodes.Status424FailedDependency,
        _ => StatusCodes.Status500InternalServerError,
    };
}
