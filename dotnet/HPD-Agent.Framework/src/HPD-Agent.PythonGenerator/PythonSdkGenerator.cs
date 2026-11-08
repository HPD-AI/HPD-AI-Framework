using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.PythonGenerator;

public sealed record PythonGeneratorOptions(
    string InputFile,
    string OutputDirectory,
    string PackageName = "hpd_agent_client");

public sealed record PythonGenerationManifest(
    string GeneratorVersion,
    string IrVersion,
    string PackageName,
    string OpenApiSha256,
    string? EventDigest,
    string InputDigest,
    string IrSha256,
    int OperationCount,
    int ModelCount,
    int EventCount,
    int InputCount,
    IReadOnlyList<string> SupportedCapabilities,
    IReadOnlyList<string> SourceCapabilities);

public sealed class PythonSdkGenerator
{
    public const string GeneratorVersion = "0.1.0";

    public async Task<PythonGenerationManifest> GenerateAsync(
        PythonGeneratorOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidatePackageName(options.PackageName);
        var contract = await OpenApiContractReader.ReadAsync(
                options.InputFile,
                cancellationToken)
            .ConfigureAwait(false);
        var ir = HpdSdkIrBuilder.Build(contract, GeneratorVersion);
        HpdSdkIrValidator.Validate(ir);
        var irJson = HpdSdkIrWriter.Write(ir);
        var names = new PythonNames(ir);
        var eventCount = ir.Unions.Single(union => union.Role == "output-event").Variants.Count;
        var inputCount = ir.Unions.Single(union => union.Role == "request-input").Variants.Count;
        var manifest = new PythonGenerationManifest(
            GeneratorVersion,
            ir.IrVersion,
            options.PackageName,
            ir.SourceOpenApiSha256,
            ir.EventDigest,
            ir.InputDigest,
            "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(irJson))).ToLowerInvariant(),
            ir.Operations.Count,
            names.Models.Count,
            eventCount,
            inputCount,
            SupportedCapabilities(ir),
            ir.Capabilities.Select(static capability => capability.Name).ToArray());

        var package = options.PackageName;
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["pyproject.toml"] = RenderProject(package),
            ["README.md"] = RenderReadme(manifest),
            ["hpd-client.generated.json"] =
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }) + "\n",
            ["sdk-ir.json"] = irJson,
            [package + "/__init__.py"] = RenderInit(ir, names, package),
            [package + "/models.py"] = RenderModels(ir, names),
            [package + "/runtime.py"] = RuntimeTemplate,
            [package + "/client.py"] = RenderClient(ir, names),
            ["conformance_runner.py"] = RenderConformanceRunner(package)
        };

        var destination = Path.GetFullPath(options.OutputDirectory);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Python output has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = destination + $".staging-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var previous = destination + $".previous-{Environment.ProcessId}-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in files)
            {
                var path = Path.Combine(staging, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (Directory.Exists(destination))
                Directory.Move(destination, previous);
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (!Directory.Exists(destination) && Directory.Exists(previous))
                    Directory.Move(previous, destination);
                throw;
            }
            if (Directory.Exists(previous))
                Directory.Delete(previous, recursive: true);
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            throw;
        }
        return manifest;
    }

    private static string RenderModels(HpdSdkIr ir, PythonNames names)
    {
        var eventIds = UnionIds(ir, "output-event");
        var inputIds = UnionIds(ir, "request-input");
        var responseIds = UnionIds(ir, "interaction-response");
        var discriminators = ir.Unions
            .SelectMany(static union => union.Variants)
            .GroupBy(static variant => variant.ComponentId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Discriminator,
                StringComparer.Ordinal);
        var builder = new StringBuilder()
            .AppendLine("from __future__ import annotations")
            .AppendLine()
            .AppendLine("from dataclasses import dataclass, fields, is_dataclass")
            .AppendLine("from enum import Enum")
            .AppendLine("from typing import Any, ClassVar, TypeVar, get_args, get_origin, get_type_hints")
            .AppendLine()
            .AppendLine("class HpdModel: pass")
            .AppendLine("class HpdAgentEvent(HpdModel): pass")
            .AppendLine("class HpdAgentInputEvent(HpdModel): pass")
            .AppendLine("class HpdAgentResponseInput(HpdAgentEvent): pass")
            .AppendLine();
        foreach (var type in ir.Types.Where(static type => type.Schema.Kind == "enum"))
        {
            builder.Append("class ").Append(names.Enums[type.ComponentId])
                .AppendLine("(str, Enum):");
            foreach (var value in type.Schema.EnumValues ?? [])
            {
                builder.Append("    ").Append(EnumMember(value)).Append(" = ")
                    .Append(PythonQuote(value)).AppendLine();
            }
            builder.AppendLine();
        }
        foreach (var type in ir.Types.Where(static type => type.Schema.ObjectModel))
        {
            var name = names.Model(type.ComponentId);
            var baseName = responseIds.Contains(type.ComponentId)
                ? "HpdAgentResponseInput"
                : eventIds.Contains(type.ComponentId)
                    ? "HpdAgentEvent"
                    : inputIds.Contains(type.ComponentId)
                        ? "HpdAgentInputEvent"
                        : "HpdModel";
            builder.Append("@dataclass(kw_only=True, slots=True)").AppendLine()
                .Append("class ").Append(name).Append('(').Append(baseName).AppendLine("):");
            discriminators.TryGetValue(type.ComponentId, out var discriminator);
            builder.Append("    WIRE_DISCRIMINATOR: ClassVar[str | None] = ")
                .Append(discriminator is null ? "None" : PythonQuote(discriminator))
                .AppendLine();
            var properties = type.Schema.Properties ?? [];
            var wireFields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal) { "wire_discriminator", "wire_fields" };
            foreach (var property in properties)
            {
                if (discriminator is not null &&
                    string.Equals(property.Name, "type", StringComparison.Ordinal))
                {
                    continue;
                }
                var fieldName = UniqueField(PythonNames.Field(property.Name), used);
                wireFields[fieldName] = property.Name;
                var annotation = names.Type(property.Type, optional: !property.Required);
                builder.Append("    ").Append(fieldName).Append(": ").Append(annotation);
                if (!property.Required)
                    builder.Append(" = None");
                builder.AppendLine();
            }
            builder.Append("    WIRE_FIELDS: ClassVar[dict[str, str]] = {")
                .Append(string.Join(
                    ", ",
                    wireFields.Select(static pair =>
                        PythonQuote(pair.Key) + ": " + PythonQuote(pair.Value))))
                .AppendLine("}");
            builder.AppendLine();
        }
        builder.Append(RuntimeHydrationTemplate
            .Replace("__EVENT_REGISTRY__", RenderEventRegistry(ir, names), StringComparison.Ordinal));
        return builder.ToString();
    }

    private static string RenderEventRegistry(HpdSdkIr ir, PythonNames names)
    {
        var eventUnion = ir.Unions.Single(union => union.Role == "output-event");
        return "{\n" + string.Join(
            "\n",
            eventUnion.Variants
                .OrderBy(static variant => variant.Discriminator, StringComparer.Ordinal)
                .Select(variant =>
                    "    " + PythonQuote(variant.Discriminator) + ": " +
                    names.Model(variant.ComponentId) + ",")) + "\n}";
    }

    private static string RenderClient(HpdSdkIr ir, PythonNames names)
    {
        var builder = new StringBuilder()
            .AppendLine("from __future__ import annotations")
            .AppendLine()
            .AppendLine("import asyncio")
            .AppendLine("from collections.abc import AsyncIterator, Iterator")
            .AppendLine("from typing import Any")
            .AppendLine()
            .AppendLine("from .models import *")
            .AppendLine("from .runtime import (")
            .AppendLine("    HpdApiError, HpdSseAppliedCursor, HpdSseMessage, HpdSseState,")
            .AppendLine("    HpdJournalRebasedError, HpdStreamError, HpdTransport,")
            .AppendLine("    _quote)")
            .AppendLine();
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            RenderService(builder, group.Key, group.ToArray(), names, async: false);
            RenderService(builder, group.Key, group.ToArray(), names, async: true);
        }
        builder.AppendLine("class HpdAgentClient:")
            .AppendLine("    def __init__(self, base_url: str, *, headers: dict[str, str] | None = None,")
            .AppendLine("                 timeout: float = 60.0, max_retries: int = 3) -> None:")
            .AppendLine("        self._transport = HpdTransport(base_url, headers=headers,")
            .AppendLine("                                     timeout=timeout, max_retries=max_retries)");
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("        self.").Append(PythonNames.Property(group.Key))
                .Append(" = ").Append(PythonNames.Service(group.Key))
                .AppendLine("(self._transport)");
        }
        builder.AppendLine()
            .AppendLine("    @property")
            .AppendLine("    def transport(self) -> HpdTransport:")
            .AppendLine("        return self._transport")
            .AppendLine()
            .AppendLine("class AsyncHpdAgentClient:")
            .AppendLine("    def __init__(self, base_url: str, *, headers: dict[str, str] | None = None,")
            .AppendLine("                 timeout: float = 60.0, max_retries: int = 3) -> None:")
            .AppendLine("        self._sync = HpdAgentClient(base_url, headers=headers,")
            .AppendLine("                                    timeout=timeout, max_retries=max_retries)");
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("        self.").Append(PythonNames.Property(group.Key))
                .Append(" = Async").Append(PythonNames.Service(group.Key))
                .Append("(self._sync.").Append(PythonNames.Property(group.Key)).AppendLine(")");
        }
        return builder.ToString();
    }

    private static void RenderService(
        StringBuilder builder,
        string tag,
        IReadOnlyList<HpdOperationIr> operations,
        PythonNames names,
        bool async)
    {
        builder.AppendLine()
            .Append("class ").Append(async ? "Async" : string.Empty)
            .Append(PythonNames.Service(tag)).AppendLine(":");
        if (async)
        {
            builder.Append("    def __init__(self, sync: ").Append(PythonNames.Service(tag))
                .AppendLine(") -> None:")
                .AppendLine("        self._sync = sync")
                .AppendLine();
        }
        else
        {
            builder.AppendLine("    def __init__(self, transport: HpdTransport) -> None:")
                .AppendLine("        self._transport = transport")
                .AppendLine();
        }
        foreach (var operation in operations)
        {
            if (async)
                RenderAsyncOperation(builder, operation, names);
            else
                RenderSyncOperation(builder, operation, names);
        }
    }

    private static void RenderSyncOperation(
        StringBuilder builder,
        HpdOperationIr operation,
        PythonNames names)
    {
        var parameters = PythonParameters(operation, names);
        var method = PythonNames.Method(operation.OperationId);
        builder.Append("    def ").Append(method).Append("(self");
        if (parameters.Count > 0)
            builder.Append(", *");
        foreach (var parameter in parameters)
        {
            builder.Append(", ").Append(parameter.Name).Append(": ").Append(parameter.Type);
            if (parameter.Optional)
                builder.Append(" = None");
        }
        if (operation.ContentKind is "sse" or "websocket")
        {
            builder.Append(", last_event_id: str | None = None");
            parameters.Add(new PythonParameter("last_event_id", "last_event_id", "str", true, "query"));
        }
        if (operation.ContentKind == "sse")
            builder.Append(") -> Iterator[HpdSseMessage]:");
        else if (operation.ContentKind == "websocket")
            builder.Append(") -> Any:");
        else
            builder.Append(") -> ").Append(ReturnType(operation, names)).Append(':');
        builder.AppendLine();
        if (operation.ContentKind == "websocket")
        {
            builder.AppendLine("        raise NotImplementedError('Provider WebSocket is not supported by this Python target yet.')");
            builder.AppendLine();
            return;
        }
        if (operation.ContentKind == "multipart")
        {
            builder.AppendLine("        raise NotImplementedError('Multipart uploads are not supported by this Python target yet.')");
            builder.AppendLine();
            return;
        }
        RenderPath(builder, operation, parameters);
        RenderQuery(builder, parameters);
        if (operation.ContentKind == "sse")
        {
            builder.Append("        yield from self._transport.stream_sse(")
                .Append(PythonQuote(operation.Method)).Append(", path, params=query,")
                .AppendLine(" last_event_id=last_event_id)");
        }
        else
        {
            var body = parameters.SingleOrDefault(static parameter => parameter.Location == "body");
            var bodyExpression = body is null
                ? "None"
                : "model_to_wire(" + body.Name + ")";
            if (operation.ContentKind == "binary")
            {
                builder.Append("        return self._transport.request_bytes(")
                    .Append(PythonQuote(operation.Method)).Append(", path, params=query,")
                    .Append(" replay_safe=").Append(operation.ReplaySafe ? "True" : "False")
                    .AppendLine(")");
            }
            else
            {
                builder.Append("        return self._transport.request_json(")
                    .Append(PythonQuote(operation.Method)).Append(", path, params=query, body=")
                    .Append(bodyExpression).Append(", response_type=")
                    .Append(ResponseTypeExpression(operation, names))
                    .Append(", replay_safe=").Append(operation.ReplaySafe ? "True" : "False")
                    .AppendLine(")");
            }
        }
        builder.AppendLine();
    }

    private static void RenderAsyncOperation(
        StringBuilder builder,
        HpdOperationIr operation,
        PythonNames names)
    {
        var parameters = PythonParameters(operation, names);
        var method = PythonNames.Method(operation.OperationId);
        builder.Append("    async def ").Append(method).Append("(self");
        if (parameters.Count > 0)
            builder.Append(", *");
        foreach (var parameter in parameters)
        {
            builder.Append(", ").Append(parameter.Name).Append(": ").Append(parameter.Type);
            if (parameter.Optional)
                builder.Append(" = None");
        }
        if (operation.ContentKind is "sse" or "websocket")
        {
            builder.Append(", last_event_id: str | None = None");
            parameters.Add(new PythonParameter("last_event_id", "last_event_id", "str", true, "query"));
        }
        if (operation.ContentKind == "sse")
            builder.Append(") -> AsyncIterator[HpdSseMessage]:");
        else
            builder.Append(") -> ").Append(ReturnType(operation, names)).Append(':');
        builder.AppendLine();
        if (operation.ContentKind == "sse")
        {
            builder.Append("        iterator = self._sync.").Append(method).Append("(")
                .Append(string.Join(
                    ", ",
                    parameters.Select(static parameter =>
                        parameter.Name + "=" + parameter.Name)))
                .AppendLine(")");
            builder.AppendLine("        while True:")
                .AppendLine("            item = await asyncio.to_thread(next, iterator, None)")
                .AppendLine("            if item is None:")
                .AppendLine("                break")
                .AppendLine("            yield item");
        }
        else
        {
            builder.Append("        return await asyncio.to_thread(self._sync.")
                .Append(method).Append(", ")
                .Append(string.Join(
                    ", ",
                    parameters.Select(static parameter =>
                        parameter.Name + "=" + parameter.Name)))
                .AppendLine(")");
        }
        builder.AppendLine();
    }

    private static List<PythonParameter> PythonParameters(
        HpdOperationIr operation,
        PythonNames names)
    {
        var result = new List<PythonParameter>();
        var used = new HashSet<string>(StringComparer.Ordinal) { "self", "path", "query", "body" };
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Location == "header")
                continue;
            var name = Unique(PythonNames.Field(parameter.Name), used);
            result.Add(new PythonParameter(
                parameter.Name,
                name,
                names.Type(parameter.Type, optional: !parameter.Required),
                !parameter.Required,
                parameter.Location));
        }
        if (operation.RequestBody is { } requestBody)
        {
            result.Add(new PythonParameter(
                "body",
                Unique("input", used),
                names.Type(requestBody.Type, optional: !requestBody.Required),
                !requestBody.Required,
                "body"));
        }
        return result;
    }

    private static void RenderPath(
        StringBuilder builder,
        HpdOperationIr operation,
        IReadOnlyList<PythonParameter> parameters)
    {
        var pathParameters = parameters
            .Where(static parameter => parameter.Location == "path")
            .ToDictionary(static parameter => parameter.WireName, StringComparer.Ordinal);
        var path = operation.Path;
        foreach (var parameter in pathParameters)
        {
            path = path.Replace(
                "{" + parameter.Key + "}",
                "{_quote(" + parameter.Value.Name + ")}",
                StringComparison.Ordinal);
        }
        builder.Append("        path = f").Append(PythonQuote(path)).AppendLine();
    }

    private static void RenderQuery(
        StringBuilder builder,
        IReadOnlyList<PythonParameter> parameters)
    {
        var queries = parameters
            .Where(static parameter => parameter.Location == "query")
            .ToArray();
        if (queries.Length == 0)
        {
            builder.AppendLine("        query: dict[str, Any] = {}");
            return;
        }
        builder.AppendLine("        query: dict[str, Any] = {");
        foreach (var query in queries)
        {
            builder.Append("            ").Append(PythonQuote(query.WireName)).Append(": ")
                .Append(query.Name).AppendLine(",");
        }
        builder.AppendLine("        }");
    }

    private static string ReturnType(HpdOperationIr operation, PythonNames names)
    {
        if (operation.ContentKind == "binary")
            return "bytes";
        if (operation.ContentKind == "multipart")
            return names.Type(operation.SuccessType ?? new HpdTypeIr("any-json"));
        if (operation.SuccessType is null)
            return "None";
        return names.Type(operation.SuccessType);
    }

    private static string ResponseTypeExpression(
        HpdOperationIr operation,
        PythonNames names)
    {
        if (operation.SuccessType is null)
            return "None";
        var type = names.Type(operation.SuccessType);
        return type is "Any" or "None" or "str" or "bool" or "int" or "float" or "bytes"
            ? type
            : type.Replace(" | None", string.Empty, StringComparison.Ordinal);
    }

    private static string RenderInit(
        HpdSdkIr ir,
        PythonNames names,
        string package) =>
        $"""
        from .client import AsyncHpdAgentClient, HpdAgentClient
        from .models import *
        from .runtime import (
            HpdApiError,
            HpdClientToolInvocation,
            HpdClientToolRegistry,
            HpdClientToolResult,
            HpdJournalRebasedError,
            HpdSseAppliedCursor,
            HpdSseMessage,
            HpdSseState,
            HpdStreamError,
        )

        __version__ = "{GeneratorVersion}"
        IR_VERSION = "{ir.IrVersion}"
        GENERATED_PACKAGE = "{package}"

        __all__ = [
            "AsyncHpdAgentClient",
            "HpdAgentClient",
            "HpdApiError",
            "HpdClientToolInvocation",
            "HpdClientToolRegistry",
            "HpdClientToolResult",
            "HpdJournalRebasedError",
            "HpdSseAppliedCursor",
            "HpdSseMessage",
            "HpdSseState",
            "HpdStreamError",
        ]
        """ + "\n";

    private static string RenderProject(string package) =>
        $$"""
        [build-system]
        requires = ["setuptools>=68"]
        build-backend = "setuptools.build_meta"

        [project]
        name = "{{package.Replace('_', '-')}}"
        version = "{{GeneratorVersion}}"
        description = "Generated HPD Agent SDK"
        requires-python = ">=3.11"
        dependencies = []

        [tool.setuptools.packages.find]
        include = ["{{package}}*"]
        """ + "\n";

    private static string RenderReadme(PythonGenerationManifest manifest) =>
        $"""
        # Generated HPD Agent Python SDK

        - Generator: `{manifest.GeneratorVersion}`
        - IR: `{manifest.IrVersion}`
        - OpenAPI SHA-256: `{manifest.OpenApiSha256}`
        - Input digest: `{manifest.InputDigest}`
        - IR SHA-256: `{manifest.IrSha256}`
        - Operations: {manifest.OperationCount}
        - Models: {manifest.ModelCount}
        - Events: {manifest.EventCount}
        - Public inputs: {manifest.InputCount}
        - Supported capabilities: {string.Join(", ", manifest.SupportedCapabilities)}

        The Python runtime includes the client-tool registry and normalized
        result model. Client-tool-provider WebSocket transport and automatic SSE
        tool dispatch are not part of the current target; unsupported source
        capabilities remain visible in `sdk-ir.json`.

        The package uses only the Python standard library. `HpdAgentClient`
        provides synchronous resource services; `AsyncHpdAgentClient` wraps them
        with asynchronous entry points. The runtime owns HTTP retries, structured
        ProblemDetails errors, SSE parsing, cursor validation, and replay
        suppression.
        """ + "\n";

    private static string RenderConformanceRunner(string package) =>
        $$"""
        from __future__ import annotations

        import argparse
        import json
        from pathlib import Path

        from {{package}} import (
            HpdClientToolInvocation,
            HpdClientToolRegistry,
            HpdClientToolResult,
            HpdJournalRebasedError,
            HpdSseAppliedCursor,
            HpdSseMessage,
            HpdSseState,
            HpdStreamError,
        )
        from {{package}}.models import chat_user_message


        def main() -> None:
            parser = argparse.ArgumentParser()
            parser.add_argument("fixture")
            args = parser.parse_args()
            document = json.loads(Path(args.fixture).read_text(encoding="utf-8"))
            executed = 0
            for fixture in document["fixtures"]:
                kind = fixture["kind"]
                if kind == "sse-state":
                    run_sse(fixture)
                elif kind == "json-shape":
                    run_shape(fixture)
                elif kind == "client-tool":
                    run_client_tool(fixture)
                else:
                    raise AssertionError(f"unsupported fixture kind {kind}")
                executed += 1
            assert executed == len(document["fixtures"])
            print(f"Python conformance passed: {executed} fixtures")


        def run_sse(fixture: dict) -> None:
            initial = fixture["initialCursor"]
            state = HpdSseState(
                HpdSseAppliedCursor(initial["generation"], initial["sequence"])
            )
            expected_error = fixture.get("expectedError")
            if expected_error:
                try:
                    for item in fixture["messages"]:
                        state.apply(_message(item), item.get("sequence"))
                except HpdStreamError as error:
                    if expected_error == "journal-rebased":
                        assert isinstance(error, HpdJournalRebasedError), type(error)
                    else:
                        assert "did not match" in str(error), error
                else:
                    raise AssertionError(f"{fixture['name']} did not fail")
            else:
                decisions = [
                    state.apply(_message(item), item.get("sequence"))
                    for item in fixture["messages"]
                ]
                assert decisions == fixture["expectedDecisions"], decisions
            if expected_cursor := fixture.get("expectedCursor"):
                assert state.cursor == HpdSseAppliedCursor(
                    expected_cursor["generation"],
                    expected_cursor["sequence"],
                )


        def run_shape(fixture: dict) -> None:
            operation = fixture["operation"]
            if operation == "run-config-empty":
                actual = {}
            elif operation == "chat-user-message":
                actual = chat_user_message(fixture["input"])
            else:
                raise AssertionError(f"unsupported operation {operation}")
            assert actual == fixture["expected"], actual


        def run_client_tool(fixture: dict) -> None:
            registry = HpdClientToolRegistry()
            if fixture.get("registration"):
                handler_name = fixture["handler"]
                registry.register(
                    fixture["registration"],
                    lambda _: HpdClientToolResult.text(handler_name),
                )
            source = fixture["invocation"]
            result = registry.invoke(HpdClientToolInvocation(
                source["requestId"],
                source["toolName"],
                source["callId"],
                source["arguments"],
            ))
            assert result.outcome == fixture["expectedOutcome"], result
            if capability := fixture.get("expectedCapability"):
                assert capability in registry.capabilities, registry.capabilities
            if content := fixture.get("expectedContent"):
                assert result.content == [content], result.content
            if message := fixture.get("expectedErrorMessageContains"):
                assert message in (result.error_message or ""), result.error_message


        def _message(fixture: dict) -> HpdSseMessage:
            return HpdSseMessage(fixture["event"], fixture["id"], fixture["data"])


        if __name__ == "__main__":
            main()
        """ + "\n";

    private static HashSet<string> UnionIds(HpdSdkIr ir, string role) =>
        ir.Unions.SingleOrDefault(union => union.Role == role)?.Variants
            .Select(static variant => variant.ComponentId)
            .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlyList<string> SupportedCapabilities(HpdSdkIr ir)
    {
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "agents",
            "audio",
            "client-tools",
            "collapsing",
            "content",
            "evaluations",
            "executions",
            "forking",
            "goals",
            "interactions",
            "json",
            "middleware",
            "multimodal-content",
            "provider-authentication",
            "runtime-extensions",
            "server-sent-events",
            "sessions",
            "threads",
            "unknown-events"
        };
        return ir.Capabilities
            .Select(static capability => capability.Name)
            .Where(supported.Contains)
            .OrderBy(static capability => capability, StringComparer.Ordinal)
            .ToArray();
    }

    private static string UniqueField(string candidate, HashSet<string> used)
    {
        if (used.Add(candidate))
            return candidate;
        for (var suffix = 2; ; suffix++)
        {
            var next = candidate + suffix;
            if (used.Add(next))
                return next;
        }
    }

    private static string Unique(string candidate, HashSet<string> used) =>
        UniqueField(candidate, used);

    private static string EnumMember(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }
        var result = builder.ToString().Trim('_');
        return result.Length == 0 ? "VALUE" : char.IsAsciiDigit(result[0]) ? "_" + result : result;
    }

    private static string PythonQuote(string value) =>
        JsonSerializer.Serialize(value);

    private static void ValidatePackageName(string package)
    {
        if (string.IsNullOrWhiteSpace(package) ||
            !char.IsAsciiLetter(package[0]) && package[0] != '_' ||
            package.Skip(1).Any(static value =>
                !char.IsAsciiLetterOrDigit(value) && value != '_'))
        {
            throw new ArgumentException($"Python package name '{package}' is invalid.");
        }
    }

    private sealed record PythonParameter(
        string WireName,
        string Name,
        string Type,
        bool Optional,
        string Location);

    private const string RuntimeHydrationTemplate =
        """

        TModel = TypeVar("TModel", bound=HpdModel)

        def hydrate_type(annotation: Any, value: Any) -> Any:
            if value is None:
                return None
            origin = get_origin(annotation)
            args = get_args(annotation)
            if origin is list and args:
                return [hydrate_type(args[0], item) for item in value]
            if origin is dict and len(args) == 2:
                return {key: hydrate_type(args[1], item) for key, item in value.items()}
            if origin is None and isinstance(annotation, type):
                if issubclass(annotation, Enum):
                    return annotation(value)
                if issubclass(annotation, HpdModel):
                    return hydrate(annotation, value)
            return value

        def hydrate(cls: type[TModel], value: Any) -> TModel | None:
            if value is None:
                return None
            if not isinstance(value, dict):
                return value
            hints = get_type_hints(cls)
            kwargs = {}
            wire_fields = getattr(cls, "WIRE_FIELDS", {})
            for field in fields(cls):
                wire_name = wire_fields.get(field.name, field.name)
                if wire_name in value:
                    kwargs[field.name] = hydrate_type(hints.get(field.name, Any), value[wire_name])
            return cls(**kwargs)

        def model_to_wire(value: Any) -> Any:
            if value is None or isinstance(value, (str, int, float, bool)):
                return value
            if isinstance(value, Enum):
                return value.value
            if isinstance(value, list):
                return [model_to_wire(item) for item in value]
            if isinstance(value, tuple):
                return [model_to_wire(item) for item in value]
            if isinstance(value, dict):
                return {key: model_to_wire(item) for key, item in value.items()}
            if is_dataclass(value):
                result = {}
                discriminator = getattr(value, "WIRE_DISCRIMINATOR", None)
                if discriminator is not None:
                    result["type"] = discriminator
                wire_fields = getattr(value, "WIRE_FIELDS", {})
                for field in fields(value):
                    item = getattr(value, field.name)
                    if item is not None:
                        result[wire_fields.get(field.name, field.name)] = model_to_wire(item)
                return result
            return value

        EVENT_REGISTRY: dict[str, type[HpdAgentEvent]] = __EVENT_REGISTRY__

        def hydrate_event(value: Any) -> HpdAgentEvent | None:
            if value is None:
                return None
            discriminator = value.get("type") if isinstance(value, dict) else None
            cls = EVENT_REGISTRY.get(discriminator)
            return hydrate(cls, value) if cls is not None else None

        def chat_user_message(text: str) -> dict[str, Any]:
            return {
                "role": "user",
                "contents": [{"$type": "text", "text": text}],
            }
        """;

    private const string RuntimeTemplate =
        """
        from __future__ import annotations

        from dataclasses import dataclass
        import json
        import time
        from typing import Any, Callable, Iterator
        from urllib.error import HTTPError, URLError
        from urllib.parse import quote, urlencode, urljoin
        from urllib.request import Request, urlopen

        from .models import hydrate_type


        class HpdApiError(Exception):
            def __init__(self, status: int, code: str, message: str, body: Any = None):
                super().__init__(message)
                self.status = status
                self.code = code
                self.problem = body
                self.errors = body.get("errors", {}) if isinstance(body, dict) else {}


        class HpdStreamError(Exception):
            pass


        class HpdJournalRebasedError(HpdStreamError):
            def __init__(self, previous_generation: int, current_generation: int):
                super().__init__(
                    f"journal generation changed from {previous_generation} to {current_generation}"
                )
                self.previous_generation = previous_generation
                self.current_generation = current_generation


        @dataclass(frozen=True)
        class HpdSseAppliedCursor:
            generation: int
            sequence: int

            def __str__(self) -> str:
                return f"{self.generation}:{self.sequence}"


        @dataclass(frozen=True)
        class HpdSseMessage:
            event: str
            id: str | None
            data: Any


        class HpdSseState:
            def __init__(self, cursor: HpdSseAppliedCursor | None = None):
                self.cursor = cursor or HpdSseAppliedCursor(0, 0)

            def apply(self, message: HpdSseMessage, sequence: int | None = None) -> str:
                if message.event == "thread-journal-rebased":
                    previous = int(message.data["previousGeneration"])
                    current = int(message.data["currentGeneration"])
                    self.cursor = HpdSseAppliedCursor(current, 0)
                    raise HpdJournalRebasedError(previous, current)
                if message.id is None:
                    return "live"
                try:
                    generation_text, sequence_text = message.id.split(":", 1)
                    generation = int(generation_text)
                    cursor_sequence = int(sequence_text)
                except (TypeError, ValueError) as error:
                    raise HpdStreamError(f"invalid event cursor {message.id!r}") from error
                if sequence is not None and sequence != cursor_sequence:
                    raise HpdStreamError(
                        f"event sequence {sequence} did not match cursor {message.id}"
                    )
                incoming = HpdSseAppliedCursor(generation, cursor_sequence)
                if incoming == self.cursor:
                    return "duplicate"
                if incoming.generation != self.cursor.generation:
                    raise HpdStreamError(
                        f"cursor generation {incoming.generation} did not match "
                        f"{self.cursor.generation}"
                    )
                if incoming.sequence <= self.cursor.sequence:
                    return "duplicate"
                self.cursor = incoming
                return "committed"


        @dataclass(frozen=True)
        class HpdClientToolInvocation:
            request_id: str
            tool_name: str
            call_id: str
            arguments: dict[str, Any]


        @dataclass(frozen=True)
        class HpdClientToolResult:
            outcome: str
            content: list[dict[str, Any]] | None = None
            error_message: str | None = None

            @staticmethod
            def text(text: str) -> "HpdClientToolResult":
                return HpdClientToolResult("Completed", [{"type": "text", "text": text}])

            @staticmethod
            def failed(message: str) -> "HpdClientToolResult":
                return HpdClientToolResult("Failed", error_message=message)


        class HpdClientToolRegistry:
            def __init__(self) -> None:
                self._handlers: dict[str, Callable[[HpdClientToolInvocation], HpdClientToolResult]] = {}
                self._fallback: Callable[[HpdClientToolInvocation], HpdClientToolResult] | None = None

            @staticmethod
            def normalize_name(value: str) -> str:
                return value.strip().rsplit(".", 1)[-1]

            def register(
                self,
                name: str,
                handler: Callable[[HpdClientToolInvocation], HpdClientToolResult],
            ) -> "HpdClientToolRegistry":
                self._handlers[self.normalize_name(name)] = handler
                return self

            def register_fallback(
                self,
                handler: Callable[[HpdClientToolInvocation], HpdClientToolResult],
            ) -> "HpdClientToolRegistry":
                self._fallback = handler
                return self

            def can_handle(self, name: str) -> bool:
                return self.normalize_name(name) in self._handlers or self._fallback is not None

            @property
            def capabilities(self) -> list[str]:
                return [f"client-tool:{name}" for name in sorted(self._handlers)]

            def invoke(self, invocation: HpdClientToolInvocation) -> HpdClientToolResult:
                handler = self._handlers.get(
                    self.normalize_name(invocation.tool_name),
                    self._fallback,
                )
                if handler is None:
                    return HpdClientToolResult.failed(
                        f"Unknown client tool: {invocation.tool_name}"
                    )
                try:
                    return handler(invocation)
                except Exception as error:
                    return HpdClientToolResult.failed(str(error) or "Client tool failed.")


        def _quote(value: Any) -> str:
            return quote(str(value), safe="")


        class HpdTransport:
            def __init__(
                self,
                base_url: str,
                *,
                headers: dict[str, str] | None = None,
                timeout: float = 60.0,
                max_retries: int = 3,
            ) -> None:
                self.base_url = base_url.rstrip("/") + "/"
                self.headers = {"Accept": "application/json", **(headers or {})}
                self.timeout = timeout
                self.max_retries = max(0, max_retries)

            def request_json(
                self,
                method: str,
                path: str,
                *,
                params: dict[str, Any] | None = None,
                body: Any = None,
                response_type: type | None = None,
                replay_safe: bool = False,
                extra_headers: dict[str, str] | None = None,
            ) -> Any:
                payload = self._request(
                    method, path, params=params, body=body,
                    replay_safe=replay_safe, extra_headers=extra_headers
                )
                if not payload:
                    return None
                value = json.loads(payload)
                return hydrate_type(response_type, value)

            def request_bytes(
                self,
                method: str,
                path: str,
                *,
                params: dict[str, Any] | None = None,
                body: Any = None,
                replay_safe: bool = False,
            ) -> bytes:
                return self._request(
                    method, path, params=params, body=body,
                    replay_safe=replay_safe, accept="application/octet-stream"
                )

            def stream_sse(
                self,
                method: str,
                path: str,
                *,
                params: dict[str, Any] | None = None,
                last_event_id: str | None = None,
            ) -> Iterator[HpdSseMessage]:
                url = self._url(path, params)
                headers = {
                    **self.headers,
                    "Accept": "text/event-stream",
                    "Cache-Control": "no-cache",
                }
                if last_event_id:
                    headers["Last-Event-ID"] = last_event_id
                request = Request(url, method=method, headers=headers)
                try:
                    with urlopen(request, timeout=self.timeout) as response:
                        event_name = None
                        event_id = None
                        data_lines: list[str] = []
                        for raw_line in response:
                            line = raw_line.decode("utf-8").rstrip("\r\n")
                            if not line:
                                if data_lines:
                                    try:
                                        data = json.loads("\n".join(data_lines))
                                    except json.JSONDecodeError:
                                        data = None
                                    if data is not None:
                                        yield HpdSseMessage(event_name or "message", event_id, data)
                                event_name = None
                                event_id = None
                                data_lines = []
                                continue
                            if line.startswith(":"):
                                continue
                            field, _, value = line.partition(":")
                            value = value[1:] if value.startswith(" ") else value
                            if field == "event":
                                event_name = value
                            elif field == "id":
                                event_id = value
                            elif field == "data":
                                data_lines.append(value)
                        if data_lines:
                            try:
                                data = json.loads("\n".join(data_lines))
                            except json.JSONDecodeError:
                                data = None
                            if data is not None:
                                yield HpdSseMessage(event_name or "message", event_id, data)
                except HTTPError as error:
                    self._raise_http_error(error)

            def _request(
                self,
                method: str,
                path: str,
                *,
                params: dict[str, Any] | None = None,
                body: Any = None,
                replay_safe: bool = False,
                extra_headers: dict[str, str] | None = None,
                accept: str = "application/json",
            ) -> bytes:
                url = self._url(path, params)
                data = None if body is None else json.dumps(
                    body, separators=(",", ":"), ensure_ascii=False
                ).encode("utf-8")
                headers = {
                    **self.headers,
                    "Accept": accept,
                    **(extra_headers or {}),
                }
                if data is not None:
                    headers["Content-Type"] = "application/json"
                idempotent = method.upper() in {"GET", "HEAD", "OPTIONS", "PUT", "DELETE"}
                retryable = idempotent or replay_safe
                attempts = self.max_retries + 1 if retryable else 1
                for attempt in range(attempts):
                    request = Request(url, data=data, method=method, headers=headers)
                    try:
                        with urlopen(request, timeout=self.timeout) as response:
                            return response.read()
                    except HTTPError as error:
                        if attempt + 1 < attempts and error.code in {409, 429, 502, 503, 504}:
                            retry_after = error.headers.get("Retry-After")
                            delay = float(retry_after) if retry_after and retry_after.isdigit() \
                                else min(0.25 * (2 ** attempt), 4.0)
                            time.sleep(delay)
                            continue
                        self._raise_http_error(error)
                    except URLError as error:
                        if attempt + 1 < attempts:
                            time.sleep(min(0.25 * (2 ** attempt), 4.0))
                            continue
                        raise HpdApiError(0, "transport-error", str(error.reason)) from error
                raise AssertionError("unreachable")

            def _url(self, path: str, params: dict[str, Any] | None) -> str:
                url = urljoin(self.base_url, path.lstrip("/"))
                query = {
                    key: value
                    for key, value in (params or {}).items()
                    if value is not None
                }
                return url + ("?" + urlencode(query, doseq=True) if query else "")

            @staticmethod
            def _raise_http_error(error: HTTPError) -> None:
                raw = error.read()
                try:
                    body = json.loads(raw) if raw else None
                except json.JSONDecodeError:
                    body = raw.decode("utf-8", errors="replace") if raw else None
                code = (
                    body.get("type")
                    if isinstance(body, dict)
                    else f"http-{error.code}"
                ) or f"http-{error.code}"
                message = (
                    body.get("detail") or body.get("title")
                    if isinstance(body, dict)
                    else str(body or error.reason)
                )
                raise HpdApiError(error.code, str(code), str(message), body) from error
        """;
}
