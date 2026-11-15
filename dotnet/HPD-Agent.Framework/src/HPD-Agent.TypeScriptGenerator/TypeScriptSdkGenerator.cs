using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.TypeScriptGenerator;

public sealed record TypeScriptGeneratorOptions(
    string InputFile,
    string OutputDirectory,
    string PackageName = "@hpd-research/hpd-agent-client");

public sealed record TypeScriptGenerationManifest(
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

public sealed class TypeScriptSdkGenerator
{
    public const string GeneratorVersion = "0.1.0";

    public async Task<TypeScriptGenerationManifest> GenerateAsync(
        TypeScriptGeneratorOptions options,
        CancellationToken cancellationToken = default)
    {
        var contract = await OpenApiContractReader.ReadAsync(
                options.InputFile,
                cancellationToken)
            .ConfigureAwait(false);
        var ir = HpdSdkIrBuilder.Build(contract, GeneratorVersion);
        HpdSdkIrValidator.Validate(ir);
        var names = new TypeScriptNames(ir);
        var irJson = HpdSdkIrWriter.Write(ir);
        var manifest = new TypeScriptGenerationManifest(
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
            ir.Unions.Single(union => union.Role == "output-event").Variants.Count,
            ir.Unions.Single(union => union.Role == "request-input").Variants.Count,
            SupportedCapabilities(ir),
            ir.Capabilities.Select(static capability => capability.Name).ToArray());
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["package.json"] = RenderPackage(options.PackageName),
            ["tsconfig.json"] = TsConfig,
            [".gitignore"] = "dist/\nnode_modules/\n",
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
            ["src/generated/models.ts"] = RenderModels(ir, names),
            ["src/generated/events.ts"] = RenderEvents(ir, names),
            ["src/generated/services.ts"] = RenderServices(ir, names),
            ["src/generated/index.ts"] = "export * from './models.js';\nexport * from './events.js';\nexport * from './services.js';\n",
            ["src/runtime.ts"] = ReadSdkSource("runtime.ts"),
            ["src/index.ts"] = "export * from './generated/index.js';\nexport * from './runtime.js';\n",
            ["conformance.mjs"] = RenderConformance()
        };

        // Reduced OpenAPI documents can still generate a direct endpoint client.
        if (SupportsSessionClient(ir))
        {
            files["src/observation.ts"] = ReadSdkSource("observation.ts");
            files["src/event-schema.ts"] = ReadSdkSource("event-schema.ts");
            files["src/generated/event-schemas.ts"] = RenderEventSchemas(ir);
            files["src/client.ts"] = ReadSdkSource("client.ts");
            files["src/index.ts"] = ReadSdkSource("index.ts");
            files["test/client.test.mjs"] = ReadSdkSource("client.test.mjs");
            files["test/types.ts"] = ReadSdkSource("types.ts");
            files["test/tsconfig.json"] = ReadSdkSource("tsconfig.json");
        }

        var destination = Path.GetFullPath(options.OutputDirectory);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("TypeScript output has no parent directory.");
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

    private static string RenderModels(HpdSdkIr ir, TypeScriptNames names)
    {
        var builder = new StringBuilder()
            .AppendLine("/* eslint-disable */")
            .AppendLine("// Generated from HPD Agent SDK IR v1.")
            .AppendLine();
        foreach (var type in ir.Types.Where(static type => type.Schema.Kind == "enum"))
        {
            var name = TypeScriptNames.Pascal(type.ComponentId);
            builder.Append("export const ").Append(name).AppendLine(" = {");
            foreach (var value in type.Schema.EnumValues ?? [])
            {
                builder.Append("  ").Append(Identifier(value)).Append(": ")
                    .Append(JsonSerializer.Serialize(value)).AppendLine(",");
            }
            builder.AppendLine("} as const;")
                .Append("export type ").Append(name).Append(" = typeof ")
                .Append(name).AppendLine("[keyof typeof " + name + "] | (string & {});")
                .AppendLine();
        }
        var discriminatorByComponent = ir.Unions
            .SelectMany(static union => union.Variants)
            .GroupBy(static variant => variant.ComponentId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Discriminator,
                StringComparer.Ordinal);
        foreach (var type in ir.Types.Where(static type => type.Schema.ObjectModel))
        {
            var name = names.Model(type.ComponentId);
            discriminatorByComponent.TryGetValue(type.ComponentId, out var discriminator);
            builder.Append("export interface ").Append(name).AppendLine(" {");
            if (discriminator is not null)
                builder.Append("  type: ").Append(JsonSerializer.Serialize(discriminator)).AppendLine(";");
            foreach (var property in type.Schema.Properties ?? [])
            {
                if (discriminator is not null &&
                    string.Equals(property.Name, "type", StringComparison.Ordinal))
                {
                    continue;
                }
                builder.Append("  ").Append(PropertyName(property.Name));
                if (!property.Required)
                    builder.Append('?');
                builder.Append(": ").Append(names.Type(property.Type, optional: !property.Required))
                    .AppendLine(";");
            }
            if (type.Schema.AllowsAdditionalProperties)
                builder.AppendLine("  [key: string]: unknown;");
            builder.AppendLine("}").AppendLine();
        }
        return builder.ToString();
    }

    private static string RenderEvents(HpdSdkIr ir, TypeScriptNames names)
    {
        var builder = new StringBuilder()
            .AppendLine("/* eslint-disable */")
            .AppendLine("import type {")
            .AppendLine("  " + string.Join(",\n  ", ir.Unions
                .SelectMany(static union => union.Variants)
                .Select(variant => names.Model(variant.ComponentId))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)))
            .AppendLine("} from './models.js';")
            .AppendLine();
        foreach (var union in ir.Unions)
        {
            var unionName = union.Role switch
            {
                "output-event" => "HpdAgentEvent",
                "request-input" => "HpdAgentInputEvent",
                "interaction-response" => "HpdAgentResponseInput",
                _ => TypeScriptNames.Pascal(union.Name) + "Union"
            };
            builder.Append("export type ").Append(unionName).Append(" =\n  | ")
                .Append(string.Join(
                    "\n  | ",
                    union.Variants
                        .Select(variant => names.Model(variant.ComponentId))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static name => name, StringComparer.Ordinal)))
                .AppendLine(";").AppendLine();
        }
        builder.AppendLine("export const EventTypes = {");
        foreach (var variant in ir.Unions.Single(union => union.Role == "output-event").Variants)
        {
            builder.Append("  ").Append(Identifier(variant.Discriminator)).Append(": ")
                .Append(JsonSerializer.Serialize(variant.Discriminator)).AppendLine(",");
        }
        builder.AppendLine("} as const;")
            .AppendLine("export type EventType = typeof EventTypes[keyof typeof EventTypes];");
        return builder.ToString();
    }

    private static string RenderServices(HpdSdkIr ir, TypeScriptNames names)
    {
        var builder = new StringBuilder()
            .AppendLine("/* eslint-disable */")
            .AppendLine("import type {")
            .AppendLine("  " + string.Join(
                ",\n  ",
                names.Models.Values
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static name => name, StringComparer.Ordinal)))
            .AppendLine("} from './models.js';")
            .AppendLine("import type { HpdAgentInputEvent, HpdAgentResponseInput } from './events.js';")
            .AppendLine("import { HpdTransport, type ClientOptions, type HpdSseMessage } from '../runtime.js';")
            .AppendLine();
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var service = TypeScriptNames.Service(group.Key);
            foreach (var operation in group.OrderBy(static operation => operation.OperationId, StringComparer.Ordinal))
                RenderArgsType(builder, operation, names, service);
            builder.Append("export class ").Append(service).AppendLine(" {")
                .AppendLine("  public constructor(private readonly transport: HpdTransport) {}")
                .AppendLine();
            foreach (var operation in group.OrderBy(static operation => operation.OperationId, StringComparer.Ordinal))
                RenderOperation(builder, operation, names, service);
            builder.AppendLine("}").AppendLine();
        }
        builder.AppendLine("export class AgentClient {")
            .AppendLine("  public readonly transport: HpdTransport;");
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("  public readonly ").Append(TypeScriptNames.PropertyForTag(group.Key))
                .Append(": ").Append(TypeScriptNames.Service(group.Key)).AppendLine(";");
        }
        builder.AppendLine()
            .AppendLine("  public constructor(baseUrl: string, options: ClientOptions = {}) {")
            .AppendLine("    this.transport = new HpdTransport(baseUrl, options);");
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("    this.").Append(TypeScriptNames.PropertyForTag(group.Key))
                .Append(" = new ").Append(TypeScriptNames.Service(group.Key))
                .AppendLine("(this.transport);");
        }
        builder.AppendLine("  }").AppendLine("}");
        return builder.ToString();
    }

    private static void RenderOperation(
        StringBuilder builder,
        HpdOperationIr operation,
        TypeScriptNames names,
        string service)
    {
        var argsType = service + TypeScriptNames.Pascal(operation.OperationId) + "Args";
        var fields = OperationFields(operation, names);
        var method = TypeScriptNames.Method(operation.OperationId);
        var returnType = operation.ContentKind switch
        {
            "sse" => "AsyncGenerator<HpdSseMessage>",
            "binary" => "ArrayBuffer",
            "empty" => "void",
            "websocket" => "never",
            _ => operation.SuccessType is null ? "void" : names.Type(operation.SuccessType)
        };
        builder.Append("  public ");
        if (operation.ContentKind == "sse")
            builder.Append("async *");
        else
            builder.Append("async ");
        builder.Append(method).Append("(args: ").Append(argsType).Append("): ");
        if (operation.ContentKind == "sse")
            builder.Append(returnType);
        else
            builder.Append("Promise<").Append(returnType).Append('>');
        builder.AppendLine(" {");
        if (operation.ContentKind == "websocket")
        {
            builder.AppendLine("    throw new Error('Provider WebSocket is not supported by this generated TypeScript target yet.');");
            builder.AppendLine("  }").AppendLine();
            return;
        }
        var path = operation.Path;
        foreach (var field in fields.Where(static field => field.Location == "path"))
        {
            path = path.Replace(
                "{" + field.WireName + "}",
                "${encodeURIComponent(String(args." + field.Name + "))}",
                StringComparison.Ordinal);
        }
        builder.Append("    const path = `").Append(path.Replace("`", "\\`", StringComparison.Ordinal))
            .AppendLine("`;");
        var queryFields = fields.Where(static field => field.Location == "query").ToArray();
        builder.Append("    const query = new URLSearchParams();");
        builder.AppendLine();
        foreach (var field in queryFields)
        {
            builder.Append("    if (args.").Append(field.Name).AppendLine(" !== undefined) {");
            builder.Append("      query.set(").Append(JsonSerializer.Serialize(field.WireName))
                .Append(", String(args.").Append(field.Name).AppendLine("));");
            builder.AppendLine("    }");
        }
        var body = fields.SingleOrDefault(static field => field.Location == "body");
        var bodyExpression = body is null ? "undefined" : "args." + body.Name;
        if (operation.ContentKind == "sse")
        {
            builder.Append("    yield* this.transport.streamSse('")
                .Append(operation.Method).AppendLine("', path, query, args.lastEventId, args);");
        }
        else if (operation.ContentKind == "multipart")
        {
            builder.Append("    return this.transport.requestMultipart<").Append(returnType).Append(">('")
                .Append(operation.Method).Append("', path, query, ").Append(bodyExpression).AppendLine(");");
        }
        else if (operation.ContentKind == "binary")
        {
            builder.Append("    return this.transport.requestBytes('")
                .Append(operation.Method).Append("', path, query, undefined, ")
                .Append(operation.ReplaySafe ? "true" : "false").AppendLine(");");
        }
        else if (operation.ContentKind == "empty")
        {
            builder.Append("    await this.transport.requestJson<void>('")
                .Append(operation.Method).Append("', path, query, ")
                .Append(bodyExpression).Append(", ")
                .Append(operation.ReplaySafe ? "true" : "false").AppendLine(");");
        }
        else
        {
            builder.Append("    return this.transport.requestJson<").Append(returnType)
                .Append(">('").Append(operation.Method).Append("', path, query, ")
                .Append(bodyExpression).Append(", ")
                .Append(operation.ReplaySafe ? "true" : "false").AppendLine(");");
        }
        builder.AppendLine("  }").AppendLine();
    }

    private static void RenderArgsType(
        StringBuilder builder,
        HpdOperationIr operation,
        TypeScriptNames names,
        string service)
    {
        var argsType = service + TypeScriptNames.Pascal(operation.OperationId) + "Args";
        builder.Append("export type ").Append(argsType).AppendLine(" = {");
        foreach (var field in OperationFields(operation, names))
        {
            builder.Append("  ").Append(field.Name);
            if (!field.Required)
                builder.Append('?');
            builder.Append(": ").Append(field.Type).AppendLine(";");
        }
        if (operation.ContentKind == "sse")
        {
            builder.AppendLine("  lastEventId?: string;")
                .AppendLine("  signal?: AbortSignal;")
                .AppendLine("  onConnect?: () => void;");
        }
        builder.AppendLine("};").AppendLine();
    }

    private static IReadOnlyList<OperationField> OperationFields(
        HpdOperationIr operation,
        TypeScriptNames names)
    {
        var result = new List<OperationField>();
        var used = new HashSet<string>(StringComparer.Ordinal) { "path", "query", "lastEventId", "signal", "onConnect" };
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Location == "header")
                continue;
            var name = Unique(TypeScriptNames.Property(parameter.Name), used);
            result.Add(new OperationField(
                parameter.Name,
                name,
                names.Type(parameter.Type, optional: !parameter.Required),
                parameter.Required,
                parameter.Location));
        }
        if (operation.RequestBody is { } body)
        {
            result.Add(new OperationField(
                "body",
                Unique("input", used),
                operation.OperationId switch
                {
                    "SubmitAgentInput" => "HpdAgentInputEvent & { version: '1.0' }",
                    "RespondToAgentRequest" => "HpdAgentResponseInput & { version: '1.0' }",
                    _ when operation.ContentKind == "multipart" => "FormData",
                    _ => names.Type(body.Type, optional: !body.Required)
                },
                body.Required,
                "body"));
        }
        return result;
    }

    private static string RenderReadme(TypeScriptGenerationManifest manifest) =>
        $"""
        # Generated HPD Agent TypeScript SDK

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

        The generated contract is the primary implementation. The hand-written
        runtime owns fetch, retries, ProblemDetails, SSE framing, cursor state,
        replay suppression, and the client-tool registry.

        Provider WebSocket currently fails explicitly in this target. The
        source capability remains visible in `sdk-ir.json`.

        The full hosted contract includes the session-first TypeScript client.
        Create a session with `client.sessions.create(...)`, then use the
        returned thread to observe routed events and send input. The client
        exposes generated endpoints through `client.api`.

        Observation resolves when the stream opens. Await `observation.done`
        for terminal stream or handler errors. Supply `onRebase` to reset your
        projection before replaying a replacement journal.
        """ + "\n";

    private static string RenderPackage(string packageName) =>
        $$"""
        {
          "name": "{{packageName}}",
          "version": "0.1.0",
          "description": "Generated HPD Agent TypeScript SDK",
          "type": "module",
          "main": "./dist/index.js",
          "module": "./dist/index.js",
          "types": "./dist/index.d.ts",
          "exports": {
            ".": {
              "types": "./dist/index.d.ts",
              "import": "./dist/index.js"
            }
          },
          "files": ["dist", "README.md", "sdk-ir.json", "hpd-client.generated.json"],
          "scripts": {
            "clean": "rm -rf dist",
            "build": "npm run clean && tsc",
            "check": "npm run build",
            "test": "npm run build && node --test test/client.test.mjs",
            "type-test": "tsc -p test/tsconfig.json",
            "conformance": "node conformance.mjs",
            "prepublishOnly": "npm run build"
          },
          "devDependencies": {
            "typescript": "^5.0.0"
          },
          "engines": {
            "node": ">=18.0.0"
          },
          "license": "FSL-1.1-ALv2"
        }
        """ + "\n";

    private static IReadOnlyList<string> SupportedCapabilities(HpdSdkIr ir)
    {
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "agents", "audio", "collapsing", "content",
            "evaluations", "executions", "forking", "goals", "interactions",
            "json", "middleware", "multimodal-content", "provider-authentication",
            "runtime-extensions", "server-sent-events", "sessions", "threads",
            "unknown-events"
        };
        return ir.Capabilities
            .Select(static capability => capability.Name)
            .Where(supported.Contains)
            .OrderBy(static capability => capability, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Identifier(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }
        var result = builder.ToString().Trim('_');
        return result.Length == 0 ? "Value" : char.IsAsciiDigit(result[0]) ? "_" + result : result;
    }

    private static string PropertyName(string value) =>
        IsIdentifier(value) ? value : JsonSerializer.Serialize(value);

    private static bool IsIdentifier(string value) =>
        value.Length > 0 &&
        (char.IsAsciiLetter(value[0]) || value[0] is '_' or '$') &&
        value.Skip(1).All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '$');

    private static string Unique(string candidate, HashSet<string> used)
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

    private sealed record OperationField(
        string WireName,
        string Name,
        string Type,
        bool Required,
        string Location);

    private const string TsConfig =
        """
        {
          "compilerOptions": {
            "target": "ES2022",
            "module": "ESNext",
            "moduleResolution": "Bundler",
            "lib": ["ES2022", "DOM", "DOM.Iterable"],
            "declaration": true,
            "declarationMap": false,
            "sourceMap": false,
            "outDir": "./dist",
            "rootDir": "./src",
            "strict": true,
            "noUncheckedIndexedAccess": true,
            "exactOptionalPropertyTypes": false,
            "noUnusedLocals": false,
            "noUnusedParameters": false,
            "esModuleInterop": true,
            "skipLibCheck": true,
            "forceConsistentCasingInFileNames": true,
            "isolatedModules": true
          },
          "include": ["src/**/*"]
        }
        """ + "\n";

    private static string RenderEventSchemas(HpdSdkIr ir)
    {
        var variants = ir.Unions.Single(union => union.Role == "output-event").Variants;
        var components = ir.Types.ToDictionary(type => type.ComponentId, type => type.Schema, StringComparer.Ordinal);
        var shapes = new SortedDictionary<string, object>(StringComparer.Ordinal);

        object Shape(HpdTypeIr type)
        {
            if (type.Reference is { } reference)
            {
                var id = reference[(reference.LastIndexOf('/') + 1)..];
                if (!shapes.ContainsKey(id))
                {
                    shapes[id] = new { kind = "any-json" };
                    shapes[id] = Shape(components[id]);
                }
            }
            return new
            {
                type.Kind,
                Nullable = type.Nullable ? (bool?)true : null,
                type.Reference,
                type.Primitive,
                type.Literal,
                type.EnumValues,
                type.Composition,
                Properties = type.Properties?.Select(property => new
                {
                    property.Name,
                    Required = property.Required ? (bool?)true : null,
                    Type = Shape(property.Type)
                }).ToArray(),
                Item = type.ItemType is null ? null : Shape(type.ItemType),
                Value = type.ValueType is null ? null : Shape(type.ValueType),
                Branches = type.Branches?.Select(Shape).ToArray()
            };
        }

        var events = variants.ToDictionary(variant => variant.Discriminator, variant => Shape(variant.Type),
            StringComparer.Ordinal);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        return "// Generated from HPD Agent SDK IR v1.\n" +
            "import type { EventSchema } from '../event-schema.js';\n" +
            "export const eventSchemas: Record<string, EventSchema> = " + JsonSerializer.Serialize(events, options) + ";\n" +
            "export const eventComponents: Record<string, EventSchema> = " + JsonSerializer.Serialize(shapes, options) + ";\n";
    }

    private static bool SupportsSessionClient(HpdSdkIr ir)
    {
        string[] requiredOperations =
        [
            "CreateSession", "GetSession", "SearchSessions", "CreateThread", "GetThread",
            "ListThreads", "ForkThread", "GetAgentThreadRuntimeState", "ObserveLiveEventsWithSse",
            "SubmitAgentInput", "RespondToAgentRequest", "GetThreadExecution", "CancelThreadExecution"
        ];
        var operations = ir.Operations.Select(static operation => operation.OperationId)
            .ToHashSet(StringComparer.Ordinal);
        return requiredOperations.All(operations.Contains);
    }

    private static string ReadSdkSource(string name)
    {
        using var stream = typeof(TypeScriptSdkGenerator).Assembly.GetManifestResourceStream("SdkSource." + name)
            ?? throw new InvalidOperationException($"Missing TypeScript SDK source '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private const string ConformanceTemplate =
        """
        import assert from 'node:assert/strict';
        import fs from 'node:fs';
        import {
          ClientToolRegistry,
          HpdJournalRebasedError,
          HpdSseState,
          HpdStreamError,
          clientToolText,
        } from './dist/runtime.js';

        const document = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
        for (const fixture of document.fixtures) {
          if (fixture.kind === 'sse-state') {
            const state = new HpdSseState(fixture.initialCursor);
            if (fixture.expectedError) {
              let error;
              try {
                for (const item of fixture.messages) state.apply(item, item.sequence ?? undefined);
              } catch (caught) {
                error = caught;
              }
              assert.ok(error);
              if (fixture.expectedError === 'journal-rebased') assert.ok(error instanceof HpdJournalRebasedError);
              else assert.ok(error instanceof HpdStreamError && error.message.includes('did not match'));
            } else {
              assert.deepEqual(
                fixture.messages.map(item => state.apply(item, item.sequence ?? undefined)),
                fixture.expectedDecisions,
              );
            }
            if (fixture.expectedCursor) assert.deepEqual(state.cursor, fixture.expectedCursor);
          } else if (fixture.kind === 'json-shape') {
            const actual = fixture.operation === 'run-config-empty'
              ? {}
              : {
                  role: 'user',
                  contents: [{ $type: 'text', text: fixture.input }],
                };
            assert.deepEqual(actual, fixture.expected);
          } else if (fixture.kind === 'client-tool') {
            const registry = new ClientToolRegistry();
            if (fixture.registration) registry.register(fixture.registration, () => clientToolText(fixture.handler));
            const result = await registry.invoke(fixture.invocation);
            assert.equal(result.outcome, fixture.expectedOutcome);
            if (fixture.expectedCapability) assert.ok(registry.capabilities.includes(fixture.expectedCapability));
            if (fixture.expectedContent) assert.deepEqual(result.content, [fixture.expectedContent]);
            if (fixture.expectedErrorMessageContains) assert.ok(result.errorMessage?.includes(fixture.expectedErrorMessageContains));
          }
        }
        console.log(`TypeScript conformance passed: ${document.fixtures.length} fixtures`);
        """;

    private static string RenderConformance() => ConformanceTemplate;
}
