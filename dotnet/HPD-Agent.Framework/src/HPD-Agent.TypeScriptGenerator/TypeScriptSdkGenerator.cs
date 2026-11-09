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
            ["src/runtime.ts"] = RuntimeTemplate,
            ["src/index.ts"] = "export * from './generated/index.js';\nexport * from './runtime.js';\n",
            ["conformance.mjs"] = RenderConformance()
        };

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
            "websocket" or "multipart" => "never",
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
        if (operation.ContentKind == "multipart")
        {
            builder.AppendLine("    throw new Error('Multipart upload is not supported by this generated TypeScript target yet.');");
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
                .Append(operation.Method).AppendLine("', path, query, args.lastEventId);");
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
            builder.AppendLine("  lastEventId?: string;");
        builder.AppendLine("};").AppendLine();
    }

    private static IReadOnlyList<OperationField> OperationFields(
        HpdOperationIr operation,
        TypeScriptNames names)
    {
        var result = new List<OperationField>();
        var used = new HashSet<string>(StringComparer.Ordinal) { "path", "query", "lastEventId" };
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
                names.Type(body.Type, optional: !body.Required),
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

        Provider WebSocket and multipart upload currently fail explicitly in this
        target. They remain visible as source capabilities in `sdk-ir.json`.

        The previous hand-written TypeScript SDK is retained as a behavioral
        reference at `../archive/hpd-agent-client-manual`. Its 131 tests remain
        the compatibility checklist for restoring advanced runtime facades as
        generated integrations.
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

    private const string RuntimeTemplate =
        """
        /* eslint-disable */
        export interface ClientOptions {
          fetch?: typeof globalThis.fetch;
          headers?: Record<string, string>;
          maxRetries?: number;
          timeoutMs?: number;
        }

        export interface HpdSseAppliedCursor {
          generation: number;
          sequence: number;
        }

        export interface HpdSseMessage {
          event: string;
          id?: string;
          data: unknown;
        }

        export class HpdApiError extends Error {
          public constructor(
            public readonly status: number,
            public readonly code: string,
            message: string,
            public readonly problem?: unknown,
          ) {
            super(message);
            this.name = 'HpdApiError';
          }
        }

        export class HpdStreamError extends Error {}

        export class HpdJournalRebasedError extends HpdStreamError {
          public constructor(
            public readonly previousGeneration: number,
            public readonly currentGeneration: number,
          ) {
            super(`journal generation changed from ${previousGeneration} to ${currentGeneration}`);
          }
        }

        export class HpdSseState {
          public constructor(public cursor: HpdSseAppliedCursor = { generation: 0, sequence: 0 }) {}

          public apply(message: HpdSseMessage, sequence?: number): 'live' | 'duplicate' | 'committed' {
            if (message.event === 'thread-journal-rebased') {
              const data = message.data as { previousGeneration?: number; currentGeneration?: number };
              const previous = Number(data?.previousGeneration ?? 0);
              const current = Number(data?.currentGeneration ?? 0);
              this.cursor = { generation: current, sequence: 0 };
              throw new HpdJournalRebasedError(previous, current);
            }
            if (!message.id) return 'live';
            const [generationText, sequenceText] = message.id.split(':', 2);
            const generation = Number(generationText);
            const cursorSequence = Number(sequenceText);
            if (sequence !== undefined && sequence !== cursorSequence) {
              throw new HpdStreamError(`event sequence ${sequence} did not match cursor ${message.id}`);
            }
            if (generation === this.cursor.generation && cursorSequence === this.cursor.sequence) return 'duplicate';
            if (generation !== this.cursor.generation) {
              throw new HpdStreamError(`cursor generation ${generation} did not match ${this.cursor.generation}`);
            }
            if (cursorSequence <= this.cursor.sequence) return 'duplicate';
            this.cursor = { generation, sequence: cursorSequence };
            return 'committed';
          }
        }

        export interface ClientToolInvocation {
          requestId: string;
          toolName: string;
          callId: string;
          arguments: Record<string, unknown>;
        }

        export interface ClientToolResult {
          outcome: 'Completed' | 'Failed';
          content?: Array<Record<string, unknown>>;
          errorMessage?: string;
        }

        export type ClientToolHandler = (invocation: ClientToolInvocation) =>
          ClientToolResult | Promise<ClientToolResult>;

        export class ClientToolRegistry {
          private readonly handlers = new Map<string, ClientToolHandler>();
          private fallback?: ClientToolHandler;

          public register(name: string, handler: ClientToolHandler): this {
            this.handlers.set(normalizeClientToolName(name), handler);
            return this;
          }

          public registerFallback(handler: ClientToolHandler): this {
            this.fallback = handler;
            return this;
          }

          public canHandle(name: string): boolean {
            return this.handlers.has(normalizeClientToolName(name)) || this.fallback !== undefined;
          }

          public get capabilities(): string[] {
            return [...this.handlers.keys()].sort().map(name => `client-tool:${name}`);
          }

          public async invoke(invocation: ClientToolInvocation): Promise<ClientToolResult> {
            const handler = this.handlers.get(normalizeClientToolName(invocation.toolName)) ?? this.fallback;
            if (!handler) return { outcome: 'Failed', errorMessage: `Unknown client tool: ${invocation.toolName}` };
            try {
              return await handler(invocation);
            } catch (error) {
              return {
                outcome: 'Failed',
                errorMessage: error instanceof Error ? error.message : String(error || 'Client tool failed.'),
              };
            }
          }
        }

        export function normalizeClientToolName(value: string): string {
          const trimmed = value.trim();
          const index = trimmed.lastIndexOf('.');
          return index >= 0 && index < trimmed.length - 1 ? trimmed.slice(index + 1) : trimmed;
        }

        export function clientToolText(text: string): ClientToolResult {
          return { outcome: 'Completed', content: [{ type: 'text', text }] };
        }

        export class HpdTransport {
          private readonly baseUrl: string;
          private readonly fetchImpl: typeof globalThis.fetch;
          private readonly headers: Record<string, string>;
          private readonly maxRetries: number;
          private readonly timeoutMs: number;

          public constructor(baseUrl: string, options: ClientOptions = {}) {
            this.baseUrl = baseUrl.replace(/\/+$/, '') + '/';
            this.fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
            this.headers = { Accept: 'application/json', ...(options.headers ?? {}) };
            this.maxRetries = Math.max(0, options.maxRetries ?? 3);
            this.timeoutMs = options.timeoutMs ?? 60_000;
          }

          public async requestJson<T>(
            method: string,
            path: string,
            query: URLSearchParams,
            body: unknown,
            replaySafe: boolean,
          ): Promise<T> {
            const response = await this.request(method, path, query, body, replaySafe, 'application/json');
            const text = await response.text();
            return text.length === 0 ? undefined as T : JSON.parse(text) as T;
          }

          public async requestBytes(
            method: string,
            path: string,
            query: URLSearchParams,
            body: unknown,
            replaySafe: boolean,
          ): Promise<ArrayBuffer> {
            const response = await this.request(method, path, query, body, replaySafe, 'application/octet-stream');
            return response.arrayBuffer();
          }

          public async *streamSse(
            method: string,
            path: string,
            query: URLSearchParams,
            lastEventId?: string,
          ): AsyncGenerator<HpdSseMessage> {
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), this.timeoutMs);
            try {
              const headers: Record<string, string> = {
                ...this.headers,
                Accept: 'text/event-stream',
                'Cache-Control': 'no-cache',
              };
              if (lastEventId) headers['Last-Event-ID'] = lastEventId;
              const url = this.url(path, query);
              const response = await this.fetchImpl(url, { method, headers, signal: controller.signal });
              if (!response.ok) throw await this.apiError(response);
              if (!response.body) return;
              const reader = response.body.getReader();
              const decoder = new TextDecoder();
              let buffer = '';
              let eventName = 'message';
              let eventId: string | undefined;
              let data: string[] = [];
              const flush = (): HpdSseMessage | undefined => {
                if (data.length === 0) return undefined;
                const raw = data.join('\n');
                data = [];
                try {
                  return { event: eventName, ...(eventId ? { id: eventId } : {}), data: JSON.parse(raw) };
                } catch {
                  return undefined;
                } finally {
                  eventName = 'message';
                  eventId = undefined;
                }
              };
              while (true) {
                const { done, value } = await reader.read();
                buffer += decoder.decode(value, { stream: !done });
                const lines = buffer.split(/\r?\n/);
                buffer = lines.pop() ?? '';
                for (const line of lines) {
                  if (line === '') {
                    const message = flush();
                    if (message) yield message;
                  } else if (line.startsWith(':')) {
                    continue;
                  } else {
                    const separator = line.indexOf(':');
                    const field = separator < 0 ? line : line.slice(0, separator);
                    const rawValue = separator < 0 ? '' : line.slice(separator + 1).replace(/^ /, '');
                    if (field === 'event') eventName = rawValue;
                    else if (field === 'id') eventId = rawValue;
                    else if (field === 'data') data.push(rawValue);
                  }
                }
                if (done) {
                  const message = flush();
                  if (message) yield message;
                  break;
                }
              }
            } finally {
              clearTimeout(timeout);
            }
          }

          private async request(
            method: string,
            path: string,
            query: URLSearchParams,
            body: unknown,
            replaySafe: boolean,
            accept: string,
          ): Promise<Response> {
            const idempotent = ['GET', 'HEAD', 'OPTIONS', 'PUT', 'DELETE'].includes(method);
            const attempts = idempotent || replaySafe ? this.maxRetries + 1 : 1;
            const headers = {
              ...this.headers,
              Accept: accept,
              ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
            };
            for (let attempt = 0; attempt < attempts; attempt++) {
              try {
                const response = await this.fetchImpl(this.url(path, query), {
                  method,
                  headers,
                  body: body === undefined ? undefined : JSON.stringify(body),
                });
                if (response.ok) return response;
                if (attempt + 1 < attempts && [409, 429, 502, 503, 504].includes(response.status)) {
                  await delay(response.headers.get('Retry-After'), attempt);
                  continue;
                }
                throw await this.apiError(response);
              } catch (error) {
                if (error instanceof HpdApiError) throw error;
                if (attempt + 1 < attempts) {
                  await delay(null, attempt);
                  continue;
                }
                throw new HpdApiError(0, 'transport-error', error instanceof Error ? error.message : String(error));
              }
            }
            throw new HpdApiError(0, 'retry-exhausted', 'request failed');
          }

          private url(path: string, query: URLSearchParams): URL {
            const url = new URL(path.replace(/^\/+/, ''), this.baseUrl);
            url.search = query.toString();
            return url;
          }

          private async apiError(response: Response): Promise<HpdApiError> {
            const text = await response.text();
            let problem: unknown;
            try {
              problem = text ? JSON.parse(text) : undefined;
            } catch {
              problem = text;
            }
            const record = problem as Record<string, unknown> | undefined;
            const code = typeof record?.type === 'string' ? record.type : `http-${response.status}`;
            const message = typeof record?.detail === 'string'
              ? record.detail
              : typeof record?.title === 'string' ? record.title : text || response.statusText;
            return new HpdApiError(response.status, code, message, problem);
          }
        }

        async function delay(retryAfter: string | null, attempt: number): Promise<void> {
          const seconds = retryAfter ? Number(retryAfter) : Number.NaN;
          const milliseconds = Number.isFinite(seconds)
            ? seconds * 1_000
            : Math.min(250 * 2 ** attempt, 4_000);
          await new Promise(resolve => setTimeout(resolve, milliseconds));
        }
        """;

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
        } from './dist/index.js';

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
