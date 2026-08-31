using System.ComponentModel;
using System.Text;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent;

/// <summary>Projects structured skill children into native HPD functions.</summary>
/// <remarks>This API is public only for source-generated registration code.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SkillCapabilityFunctionProjector
{
    /// <summary>Creates resource and script functions declared by a skill.</summary>
    public static IEnumerable<AIFunction> CreateChildren(
        Skill skill,
        CapabilityId skillId,
        HPDToolSerializationOptions? serialization = null)
    {
        ArgumentNullException.ThrowIfNull(skill);

        foreach (var capability in skill.Capabilities)
        {
            switch (capability)
            {
                case SkillResource resource:
                    yield return CreateResource(skill.Name, skillId, resource, serialization);
                    break;
                case SkillScript script:
                    ValidateScript(script);
                    yield return CreateScript(skill.Name, skillId, script, serialization);
                    break;
            }
        }
    }

    private static AIFunction CreateResource(
        string skillName,
        CapabilityId skillId,
        SkillResource resource,
        HPDToolSerializationOptions? serialization)
    {
        var capabilityId = CapabilityId.Create($"{skillId.Value}:{resource.Name}");
        return HPDAIFunctionFactory.Create(
            async (_, functionContext, cancellationToken) =>
            {
                await functionContext.PublishAsync(
                    new SkillResourceReadStartedEvent(capabilityId, resource.Name),
                    cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await resource.ReadAsync(
                        new SkillResourceContext(
                            skillName,
                            functionContext,
                            functionContext.Services,
                            functionContext.ContentStore),
                        cancellationToken).ConfigureAwait(false);
                    await functionContext.PublishAsync(
                        new SkillResourceReadCompletedEvent(capabilityId, resource.Name),
                        cancellationToken).ConfigureAwait(false);
                    return result;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await functionContext.PublishAsync(
                        new SkillResourceReadFailedEvent(
                            capabilityId,
                            resource.Name,
                            exception.GetType().Name),
                        CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            },
            CreateOptions(
                resource.Name,
                resource.Description,
                resource.ResultType,
                HPDCapabilityKind.SkillResource,
                skillId,
                requiresPermission: false,
                serialization));
    }

    private static AIFunction CreateScript(
        string skillName,
        CapabilityId skillId,
        SkillScript script,
        HPDToolSerializationOptions? serialization)
    {
        var capabilityId = CapabilityId.Create($"{skillId.Value}:{script.Name}");
        return HPDAIFunctionFactory.Create(
            async (arguments, functionContext, cancellationToken) =>
            {
                var boundInput = arguments.GetBoundInput();
                var scriptArguments = new SkillScriptArguments(
                    boundInput.EffectiveJson,
                    boundInput.Value,
                    script.InputContract.BoundType,
                    script.InputContract.CanonicalSchemaFingerprint);
                var runner = functionContext.Services?
                    .GetServices<ISkillScriptRunner>()
                    .FirstOrDefault(candidate => candidate.CanRun(script));
                if (runner is null)
                {
                    await functionContext.PublishAsync(
                        new SkillScriptFailedEvent(
                            capabilityId,
                            script.Name,
                            SkillScriptErrorCategory.RunnerUnavailable),
                        cancellationToken).ConfigureAwait(false);
                    throw new SkillScriptExecutionException(
                        SkillScriptErrorCategory.RunnerUnavailable,
                        $"No registered skill script runner supports runtime '{script.Reference.Runtime}'.");
                }

                await functionContext.PublishAsync(
                    new SkillScriptStartedEvent(
                        capabilityId,
                        script.Name,
                        runner.GetType().FullName ?? runner.GetType().Name),
                    cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await ExecuteScriptAsync(
                        runner,
                        new SkillScriptExecutionContext(
                            skillName,
                            script,
                            scriptArguments,
                            functionContext,
                            functionContext.Services,
                            script.ContentStore ?? functionContext.ContentStore),
                        cancellationToken).ConfigureAwait(false);
                    await functionContext.PublishAsync(
                        new SkillScriptCompletedEvent(capabilityId, script.Name),
                        cancellationToken).ConfigureAwait(false);
                    return result;
                }
                catch (SkillScriptExecutionException exception)
                {
                    AgentEvent failure = exception.Category == SkillScriptErrorCategory.TimedOut
                        ? new SkillScriptTimedOutEvent(capabilityId, script.Name)
                        : new SkillScriptFailedEvent(capabilityId, script.Name, exception.Category);
                    await functionContext.PublishAsync(failure, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            },
            CreateOptions(
                script.Name,
                script.Description,
                typeof(object),
                HPDCapabilityKind.SkillScript,
                skillId,
                script.RequiresPermission,
                serialization,
                script.InputContract));
    }

    internal static async ValueTask<object?> ExecuteScriptAsync(
        ISkillScriptRunner runner,
        SkillScriptExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(context);
        ValidateScript(context.Script);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Script.Timeout);
        object? result;
        try
        {
            result = await runner.RunAsync(context, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new SkillScriptExecutionException(
                SkillScriptErrorCategory.TimedOut,
                $"Skill script '{context.Script.Name}' exceeded its {context.Script.Timeout} timeout.",
                exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not SkillScriptExecutionException)
        {
            throw new SkillScriptExecutionException(
                SkillScriptErrorCategory.ExecutionFailed,
                $"Skill script '{context.Script.Name}' execution failed.",
                exception);
        }
        EnsureAotSafeResult(context.Script, result);
        EnsureOutputLimit(context.Script, result);
        return result;
    }

    private static HPDAIFunctionFactoryOptions CreateOptions(
        string name,
        string description,
        Type resultType,
        HPDCapabilityKind kind,
        CapabilityId skillId,
        bool requiresPermission,
        HPDToolSerializationOptions? serialization,
        SkillScriptInputContract? inputContract = null)
        => new()
        {
            Name = name,
            Description = description,
            FunctionPermission = requiresPermission
                ? new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = true,
                    Authority = $"skill/{Uri.EscapeDataString(name)}",
                    Source = PermissionDeclarationSource.FunctionAttribute
                }
                : null,
            SchemaProvider = inputContract is null
                ? static () =>
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
                    return document.RootElement.Clone();
                }
                : () => inputContract.JsonSchema,
            ArgumentBinder = inputContract is null ? null : inputContract.Bind,
            SerializerOptions = serialization?.SerializerOptions,
            ResultType = resultType,
            AdditionalProperties = new Dictionary<string, object>
            {
                [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata
                {
                    Id = CapabilityId.Create($"{skillId.Value}:{name}"),
                    Kind = kind,
                    ParentContainerIds = [skillId]
                }
            }
        };

    private static void ValidateScript(SkillScript script)
    {
        ArgumentNullException.ThrowIfNull(script.Reference);
        ArgumentNullException.ThrowIfNull(script.InputContract);
        if (script.Timeout <= TimeSpan.Zero)
            throw new InvalidOperationException($"Skill script '{script.Name}' must have a positive timeout.");
        if (script.MaximumOutputBytes <= 0)
            throw new InvalidOperationException($"Skill script '{script.Name}' must have a positive output limit.");
    }

    private static void EnsureOutputLimit(SkillScript script, object? result)
    {
        var byteCount = result switch
        {
            null => 0,
            string text => Encoding.UTF8.GetByteCount(text),
            JsonElement json => Encoding.UTF8.GetByteCount(json.GetRawText()),
            _ => Encoding.UTF8.GetByteCount(result.ToString() ?? string.Empty)
        };
        if (byteCount > script.MaximumOutputBytes)
            throw new SkillScriptExecutionException(
                SkillScriptErrorCategory.OutputTooLarge,
                $"Skill script '{script.Name}' exceeded its {script.MaximumOutputBytes}-byte output limit.");
    }

    private static void EnsureAotSafeResult(SkillScript script, object? result)
    {
        if (result is null or string or JsonElement or ToolResultPayload or AIContent or IEnumerable<AIContent>)
            return;
        throw new SkillScriptExecutionException(
            SkillScriptErrorCategory.UnsupportedResult,
            $"Skill script '{script.Name}' returned unsupported result type '{result.GetType().FullName}'. " +
            "Return string, JsonElement, ToolResultPayload, AIContent, or IEnumerable<AIContent>." );
    }
}
