using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.GoGenerator;

public sealed record GoGeneratorOptions(
    string InputFile,
    string OutputDirectory,
    string ModulePath = "github.com/hpd-research/hpd-agent-sdk",
    string PackageName = "hpdagent");

public sealed record GoGenerationManifest(
    string GeneratorVersion,
    string IrVersion,
    string ModulePath,
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

public sealed class GoSdkGenerator
{
    public const string GeneratorVersion = "0.1.0";

    public async Task<GoGenerationManifest> GenerateAsync(
        GoGeneratorOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(options.PackageName);
        var contract = await OpenApiContractReader.ReadAsync(
                options.InputFile,
                cancellationToken)
            .ConfigureAwait(false);
        var ir = HpdSdkIrBuilder.Build(contract, GeneratorVersion);
        HpdSdkIrValidator.Validate(ir);
        var irJson = HpdSdkIrWriter.Write(ir);
        var names = new GoNames(ir);
        var eventCount = ir.Unions.Single(union => union.Role == "output-event").Variants.Count;
        var inputCount = ir.Unions.Single(union => union.Role == "request-input").Variants.Count;
        var manifest = new GoGenerationManifest(
            GeneratorVersion,
            ir.IrVersion,
            options.ModulePath,
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
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["go.mod"] = $"module {options.ModulePath}\n\ngo 1.22\n",
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
            ["models.go"] = RenderModels(ir, names, options.PackageName),
            ["sdk.go"] = RenderSdk(
                ir,
                names,
                options.PackageName,
                options.ModulePath),
            ["conformance/main.go"] = RenderConformance(options.ModulePath),
            ["runtime.go"] = RuntimeTemplate.Replace(
                "__PACKAGE__",
                options.PackageName,
                StringComparison.Ordinal)
        };
        var tests = RenderGoTests(ir, names, options.PackageName);
        if (tests.Length > 0)
            files["sdk_test.go"] = tests;

        var destination = Path.GetFullPath(options.OutputDirectory);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Go output has no parent directory.");
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

    private static string RenderModels(HpdSdkIr ir, GoNames names, string package)
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
            .Append("package ").Append(package).AppendLine()
            .AppendLine()
            .AppendLine("type HpdAgentEvent interface { isHpdAgentEvent() }")
            .AppendLine("type HpdAgentInputEvent interface { isHpdAgentInputEvent() }")
            .AppendLine("type HpdAgentResponseInput interface {")
            .AppendLine("\tisHpdAgentEvent()")
            .AppendLine("\tisHpdAgentResponseInput()")
            .AppendLine("}")
            .AppendLine();
        foreach (var type in ir.Types.Where(static type => type.Schema.Kind == "enum"))
        {
            var name = GoNames.Pascal(type.ComponentId);
            builder.Append("type ").Append(name).AppendLine(" string")
                .AppendLine()
                .AppendLine("const (");
            foreach (var value in type.Schema.EnumValues ?? [])
            {
                builder.Append('\t').Append(name).Append(EnumMember(value))
                    .Append(' ').Append(name).Append(" = ").Append(GoQuote(value))
                    .AppendLine();
            }
            builder.AppendLine(")").AppendLine();
        }
        foreach (var type in ir.Types.Where(static type => type.Schema.ObjectModel))
        {
            var name = names.Model(type.ComponentId);
            builder.Append("type ").Append(name).AppendLine(" struct {");
            discriminators.TryGetValue(type.ComponentId, out var discriminator);
            foreach (var property in type.Schema.Properties ?? [])
            {
                if (discriminator is not null &&
                    string.Equals(property.Name, "type", StringComparison.Ordinal))
                {
                    continue;
                }
                builder.Append('\t').Append(GoNames.Field(property.Name)).Append(' ')
                    .Append(names.Type(property.Type, optional: !property.Required))
                    .Append(" `json:").Append(GoQuote(property.Name))
                    .Append(",omitempty\"`").AppendLine();
            }
            builder.AppendLine("}").AppendLine();
            if (eventIds.Contains(type.ComponentId))
                builder.Append("func (*").Append(name).AppendLine(") isHpdAgentEvent() {}");
            if (inputIds.Contains(type.ComponentId))
                builder.Append("func (*").Append(name).AppendLine(") isHpdAgentInputEvent() {}");
            if (responseIds.Contains(type.ComponentId))
                builder.Append("func (*").Append(name).AppendLine(") isHpdAgentResponseInput() {}");
            builder.AppendLine();
        }
        builder.AppendLine("func DecodeHpdAgentEvent(data []byte) (HpdAgentEvent, error) {")
            .AppendLine("\tvar envelope struct { Type string `json:\"type\"` }")
            .AppendLine("\tif err := json.Unmarshal(data, &envelope); err != nil { return nil, err }")
            .AppendLine("\tswitch envelope.Type {");
        foreach (var variant in ir.Unions.Single(union => union.Role == "output-event").Variants)
        {
            builder.Append("\tcase ").Append(GoQuote(variant.Discriminator)).AppendLine(":");
            builder.Append("\t\tvar value ").Append(names.Model(variant.ComponentId)).AppendLine();
            builder.AppendLine("\t\tif err := json.Unmarshal(data, &value); err != nil { return nil, err }");
            builder.AppendLine("\t\treturn &value, nil");
        }
        builder.AppendLine("\tdefault:")
            .AppendLine("\t\treturn nil, fmt.Errorf(\"unknown HPD event discriminator %q\", envelope.Type)")
            .AppendLine("\t}")
            .AppendLine("}")
            .AppendLine()
            .AppendLine("func ChatUserMessage(text string) map[string]any {")
            .AppendLine("\treturn map[string]any{\"role\": \"user\", \"contents\": []any{")
            .AppendLine("\t\tmap[string]any{\"$type\": \"text\", \"text\": text},")
            .AppendLine("\t}}")
            .AppendLine("}");
        return "package " + package + "\n\nimport (\n\t\"encoding/json\"\n\t\"fmt\"\n)\n\n" +
               builder.ToString()
                   .Replace("package " + package + "\n\n", string.Empty, StringComparison.Ordinal);
    }

    private static string RenderSdk(
        HpdSdkIr ir,
        GoNames names,
        string package,
        string modulePath)
    {
        var builder = new StringBuilder()
            .Append("package ").Append(package).AppendLine()
            .AppendLine()
            .AppendLine("import (")
            .AppendLine("\t\"context\"")
            .AppendLine("\t\"fmt\"")
            .AppendLine("\t\"net/url\"")
            .AppendLine(")")
            .AppendLine()
            .AppendLine("type Client struct {")
            .AppendLine("\tTransport *Transport");
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            builder.Append('\t').Append(GoNames.Property(group.Key)).Append(" *")
                .Append(GoNames.Service(group.Key)).AppendLine();
        }
        builder.AppendLine("}").AppendLine()
            .AppendLine("func NewClient(baseURL string, options ClientOptions) *Client {")
            .AppendLine("\ttransport := NewTransport(baseURL, options)")
            .AppendLine("\tclient := &Client{Transport: transport}");
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("\tclient.").Append(GoNames.Property(group.Key)).Append(" = &")
                .Append(GoNames.Service(group.Key)).AppendLine("{transport: transport}");
        }
        builder.AppendLine("\treturn client")
            .AppendLine("}")
            .AppendLine();
        foreach (var group in ir.Operations
                     .GroupBy(static operation => operation.Tag, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("type ").Append(GoNames.Service(group.Key))
                .AppendLine(" struct { transport *Transport }");
            foreach (var operation in group.OrderBy(static operation => operation.OperationId, StringComparer.Ordinal))
                RenderOperation(builder, operation, names);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static void RenderOperation(
        StringBuilder builder,
        HpdOperationIr operation,
        GoNames names)
    {
        var method = GoNames.Method(operation.OperationId);
        var parameters = GoParameters(operation, names);
        var returnType = operation.ContentKind switch
        {
            "sse" => "<-chan HpdSseMessage",
            "binary" => "[]byte",
            "websocket" => "struct{}",
            "empty" => "",
            _ => names.Type(operation.SuccessType ?? new HpdTypeIr("any-json"))
        };
        var errorReturn = returnType == string.Empty ? "error" : $"({returnType}, error)";
        var service = GoNames.Service(operation.Tag);
        builder.Append("func (s *").Append(service).Append(") ").Append(method)
            .Append("(ctx context.Context");
        foreach (var parameter in parameters)
        {
            builder.Append(", ").Append(parameter.Name).Append(' ')
                .Append(parameter.Type);
        }
        builder.Append(") ").Append(errorReturn).AppendLine(" {");
        if (operation.ContentKind == "websocket")
        {
            builder.AppendLine("\treturn struct{}{}, fmt.Errorf(\"provider WebSocket is not supported by this Go target yet\")")
                .AppendLine("}");
            return;
        }
        if (operation.ContentKind == "multipart")
        {
            builder.Append("\treturn ").Append(ZeroValue(returnType))
                .AppendLine(", fmt.Errorf(\"multipart uploads are not supported by this Go target yet\")")
                .AppendLine("}");
            return;
        }
        var pathParameters = parameters.Where(static parameter => parameter.Location == "path").ToArray();
        var goPath = operation.Path;
        foreach (var parameter in pathParameters)
            goPath = goPath.Replace(
                "{" + parameter.WireName + "}",
                "%s",
                StringComparison.Ordinal);
        builder.Append("\tpath := fmt.Sprintf(").Append(GoQuote(goPath));
        foreach (var parameter in pathParameters)
            builder.Append(", url.PathEscape(fmt.Sprint(").Append(parameter.Name).Append("))");
        builder.AppendLine(")");
        builder.AppendLine("\tquery := url.Values{}");
        foreach (var parameter in parameters.Where(static parameter => parameter.Location == "query"))
        {
            builder.Append("\tif ").Append(parameter.Name).AppendLine(" != nil {");
            if (parameter.Type.StartsWith("*", StringComparison.Ordinal))
            {
                builder.Append("\t\tquery.Set(").Append(GoQuote(parameter.WireName))
                    .Append(", fmt.Sprint(*").Append(parameter.Name).AppendLine("))");
            }
            else
            {
                builder.Append("\t\tquery.Set(").Append(GoQuote(parameter.WireName))
                    .Append(", fmt.Sprint(").Append(parameter.Name).AppendLine("))");
            }
            builder.AppendLine("\t}");
        }
        var body = parameters.SingleOrDefault(static parameter => parameter.Location == "body");
        var bodyExpression = body is null ? "nil" : body.Name;
        if (operation.ContentKind == "sse")
        {
            builder.Append("\treturn s.transport.StreamSSE(ctx, ")
                .Append(GoQuote(operation.Method)).Append(", path, query, \"\")")
                .AppendLine();
        }
        else if (operation.ContentKind == "binary")
        {
            builder.Append("\treturn s.transport.RequestBytes(ctx, ")
                .Append(GoQuote(operation.Method)).Append(", path, query, nil, ")
                .Append(operation.ReplaySafe ? "true" : "false").AppendLine(")");
        }
        else if (operation.SuccessType is null || operation.ContentKind == "empty")
        {
            builder.Append("\treturn s.transport.RequestNoContent(ctx, ")
                .Append(GoQuote(operation.Method)).Append(", path, query, ")
                .Append(bodyExpression).Append(", ")
                .Append(operation.ReplaySafe ? "true" : "false").AppendLine(")");
        }
        else
        {
            var type = names.Type(operation.SuccessType);
            builder.Append("\tvar result ").Append(type).AppendLine();
            builder.Append("\tif err := s.transport.RequestJSON(ctx, ")
                .Append(GoQuote(operation.Method)).Append(", path, query, ")
                .Append(bodyExpression).Append(", &result, ")
                .Append(operation.ReplaySafe ? "true" : "false")
                .AppendLine("); err != nil {");
            builder.Append("\t\treturn ").Append(ZeroValue(type)).AppendLine(", err")
                .AppendLine("\t}");
            builder.Append("\treturn result, nil").AppendLine();
        }
        builder.AppendLine("}").AppendLine();
    }

    private static List<GoParameter> GoParameters(HpdOperationIr operation, GoNames names)
    {
        var result = new List<GoParameter>();
        var used = new HashSet<string>(StringComparer.Ordinal) { "ctx", "path", "query", "body" };
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Location == "header")
                continue;
            var name = Unique(GoNames.Field(parameter.Name).Length == 0
                ? "value"
                : char.ToLowerInvariant(GoNames.Field(parameter.Name)[0]) +
                  GoNames.Field(parameter.Name)[1..], used);
            var type = names.Type(parameter.Type, optional: !parameter.Required);
            if (parameter.Location == "query" && !type.StartsWith("*", StringComparison.Ordinal) &&
                type is not "any" and not "[]byte" && !type.StartsWith("[]", StringComparison.Ordinal) &&
                !type.StartsWith("map[", StringComparison.Ordinal))
            {
                type = "*" + type;
            }
            result.Add(new GoParameter(parameter.Name, name, type, parameter.Location));
        }
        if (operation.RequestBody is { } body)
        {
            result.Add(new GoParameter(
                "body",
                Unique("body", used),
                names.Type(body.Type, optional: false),
                "body"));
        }
        return result;
    }

    private static string ZeroValue(string type) =>
        type.StartsWith("*", StringComparison.Ordinal) ||
        type.StartsWith("[]", StringComparison.Ordinal) ||
        type.StartsWith("map[", StringComparison.Ordinal)
            ? "nil"
            : type == "any" ? "nil" : type + "{}";

    private static string RenderReadme(GoGenerationManifest manifest) =>
        $"""
        # Generated HPD Agent Go SDK

        - Generator: `{manifest.GeneratorVersion}`
        - IR: `{manifest.IrVersion}`
        - Module: `{manifest.ModulePath}`
        - OpenAPI SHA-256: `{manifest.OpenApiSha256}`
        - Input digest: `{manifest.InputDigest}`
        - IR SHA-256: `{manifest.IrSha256}`
        - Operations: {manifest.OperationCount}
        - Models: {manifest.ModelCount}
        - Events: {manifest.EventCount}
        - Public inputs: {manifest.InputCount}
        - Supported capabilities: {string.Join(", ", manifest.SupportedCapabilities)}

        The Go runtime owns HTTP retries, ProblemDetails errors, typed JSON
        decoding, SSE framing, cursor validation, and replay suppression.
        The runtime includes the client-tool registry and normalized result
        model. Provider WebSocket and automatic SSE tool dispatch are not
        implemented in this target and remain explicit source capabilities in
        `sdk-ir.json`.
        """ + "\n";

    private static string RenderGoTests(HpdSdkIr ir, GoNames names, string package)
    {
        var getSession = ir.Operations.SingleOrDefault(static operation =>
            string.Equals(operation.OperationId, "GetSession", StringComparison.Ordinal));
        if (getSession?.SuccessType is null ||
            names.ReferencedModelName(getSession.SuccessType) is null)
        {
            return string.Empty;
        }
        return $$"""
        package {{package}}

        import (
        	"context"
        	"encoding/json"
        	"net/http"
        	"net/http/httptest"
        	"strings"
        	"testing"
        )

        func TestGeneratedGetSessionHydratesAndEscapesPath(t *testing.T) {
        	var path string
        	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        		path = r.URL.EscapedPath()
        		w.Header().Set("Content-Type", "application/json")
        		_ = json.NewEncoder(w).Encode(map[string]any{
        			"id": "session-one",
        			"createdAt": "2026-01-01T00:00:00Z",
        			"lastActivity": "2026-01-01T00:00:01Z",
        			"metadata": map[string]any{},
        		})
        	}))
        	defer server.Close()
        	client := NewClient(server.URL, ClientOptions{})
        	session, err := client.Sessions.GetSession(context.Background(), "session/one")
        	if err != nil { t.Fatal(err) }
        	if session.Id != "session-one" { t.Fatalf("unexpected ID %q", session.Id) }
        	if !strings.Contains(path, "session%2Fone") { t.Fatalf("path was not escaped: %s", path) }
        }

        func TestReplaySafeGetRetriesTransientResponse(t *testing.T) {
        	attempts := 0
        	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        		attempts++
        		if attempts == 1 {
        			http.Error(w, `{"title":"busy"}`, http.StatusServiceUnavailable)
        			return
        		}
        		w.Header().Set("Content-Type", "application/json")
        		_, _ = w.Write([]byte(`{"value":"ok"}`))
        	}))
        	defer server.Close()
        	transport := NewTransport(server.URL, ClientOptions{MaxRetries: 1})
        	var result struct { Value string `json:"value"` }
        	if err := transport.RequestJSON(context.Background(), http.MethodGet, "/probe", nil, nil, &result, false); err != nil {
        		t.Fatal(err)
        	}
        	if result.Value != "ok" || attempts != 2 { t.Fatalf("value=%q attempts=%d", result.Value, attempts) }
        }
        """ + "\n";
    }

    private static string RenderConformance(string modulePath) =>
        $$"""
        package main

        import (
        	"encoding/json"
        	"errors"
        	"fmt"
        	"os"
        	"reflect"

        	sdk "{{modulePath}}"
        )

        type cursor struct {
        	Generation int   `json:"generation"`
        	Sequence   int64 `json:"sequence"`
        }

        type fixtureMessage struct {
        	Event    string          `json:"event"`
        	ID       *string         `json:"id"`
        	Sequence *int64          `json:"sequence"`
        	Data     json.RawMessage `json:"data"`
        }

        type fixture struct {
        	Name             string           `json:"name"`
        	Kind             string           `json:"kind"`
        	InitialCursor    cursor           `json:"initialCursor"`
        	Messages         []fixtureMessage `json:"messages"`
        	ExpectedDecisions []string        `json:"expectedDecisions"`
        	ExpectedCursor   *cursor          `json:"expectedCursor"`
        	ExpectedError    string           `json:"expectedError"`
        	Operation        string           `json:"operation"`
        	Input            string           `json:"input"`
        	Expected         any              `json:"expected"`
        	Registration     *string          `json:"registration"`
        	Handler          *string          `json:"handler"`
        	Invocation       struct {
        		RequestID string         `json:"requestId"`
        		ToolName  string         `json:"toolName"`
        		CallID    string         `json:"callId"`
        		Arguments map[string]any `json:"arguments"`
        	} `json:"invocation"`
        	ExpectedOutcome                string `json:"expectedOutcome"`
        	ExpectedCapability             string `json:"expectedCapability"`
        	ExpectedContent                any    `json:"expectedContent"`
        	ExpectedErrorMessageContains   string `json:"expectedErrorMessageContains"`
        }

        type fixtureDocument struct {
        	Fixtures []fixture `json:"fixtures"`
        }

        func main() {
        	if len(os.Args) != 2 {
        		panic("usage: go run ./conformance <fixtures.json>")
        	}
        	data, err := os.ReadFile(os.Args[1])
        	if err != nil { panic(err) }
        	var document fixtureDocument
        	if err := json.Unmarshal(data, &document); err != nil { panic(err) }
        	for _, item := range document.Fixtures {
        		switch item.Kind {
        		case "sse-state": runSse(item)
        		case "json-shape": runShape(item)
        		case "client-tool": runClientTool(item)
        		default: panic("unsupported fixture kind " + item.Kind)
        		}
        	}
        	fmt.Printf("Go conformance passed: %d fixtures\n", len(document.Fixtures))
        }

        func runSse(item fixture) {
        	state := sdk.NewHpdSseState(sdk.HpdSseAppliedCursor{
        		Generation: item.InitialCursor.Generation,
        		Sequence: item.InitialCursor.Sequence,
        	})
        	if item.ExpectedError != "" {
        		var streamErr error
        		for _, value := range item.Messages {
        			if _, streamErr = state.Apply(sdk.HpdSseMessage{
        				Event: value.Event, ID: value.ID, Data: value.Data,
        			}, value.Sequence); streamErr != nil { break }
        		}
        		if streamErr == nil { panic(item.Name + " did not fail") }
        		var rebase *sdk.HpdJournalRebasedError
        		if item.ExpectedError == "journal-rebased" {
        			if !errors.As(streamErr, &rebase) { panic(fmt.Sprintf("%s: %T", item.Name, streamErr)) }
        		} else if item.ExpectedError == "cursor-sequence-mismatch" {
        			if errors.As(streamErr, &rebase) || !contains(streamErr.Error(), "did not match") {
        				panic(fmt.Sprintf("%s: %v", item.Name, streamErr))
        			}
        		}
        	} else {
        		decisions := make([]string, 0, len(item.Messages))
        		for _, value := range item.Messages {
        			decision, err := state.Apply(sdk.HpdSseMessage{
        				Event: value.Event, ID: value.ID, Data: value.Data,
        			}, value.Sequence)
        			if err != nil { panic(err) }
        			decisions = append(decisions, decision)
        		}
        		if !reflect.DeepEqual(decisions, item.ExpectedDecisions) {
        			panic(fmt.Sprintf("%s: %v", item.Name, decisions))
        		}
        	}
        	if item.ExpectedCursor != nil {
        		expected := sdk.HpdSseAppliedCursor{
        			Generation: item.ExpectedCursor.Generation,
        			Sequence: item.ExpectedCursor.Sequence,
        		}
        		if state.Cursor() != expected { panic(fmt.Sprintf("%s: %v", item.Name, state.Cursor())) }
        	}
        }

        func runShape(item fixture) {
        	var actual any
        	switch item.Operation {
        	case "run-config-empty": actual = map[string]any{}
        	case "chat-user-message": actual = sdk.ChatUserMessage(item.Input)
        	default: panic("unsupported operation " + item.Operation)
        	}
        	if !reflect.DeepEqual(actual, item.Expected) {
        		panic(fmt.Sprintf("%s: %#v", item.Name, actual))
        	}
        }

        func runClientTool(item fixture) {
        	registry := sdk.NewClientToolRegistry()
        	if item.Registration != nil && item.Handler != nil {
        		handlerName := *item.Handler
        		registry.Register(*item.Registration, func(sdk.ClientToolInvocation) sdk.ClientToolResult {
        			return sdk.ClientToolText(handlerName)
        		})
        	}
        	result := registry.Invoke(sdk.ClientToolInvocation{
        		RequestID: item.Invocation.RequestID,
        		ToolName: item.Invocation.ToolName,
        		CallID: item.Invocation.CallID,
        		Arguments: item.Invocation.Arguments,
        	})
        	if result.Outcome != item.ExpectedOutcome {
        		panic(fmt.Sprintf("%s: %s", item.Name, result.Outcome))
        	}
        	if item.ExpectedCapability != "" {
        		found := false
        		for _, capability := range registry.Capabilities() {
        			if capability == item.ExpectedCapability { found = true; break }
        		}
        		if !found { panic(item.Name + ": missing capability") }
        	}
        	if item.ExpectedContent != nil {
        		if len(result.Content) != 1 || !reflect.DeepEqual(any(result.Content[0]), item.ExpectedContent) {
        			panic(fmt.Sprintf("%s: %#v", item.Name, result.Content))
        		}
        	}
        	if item.ExpectedErrorMessageContains != "" && !contains(result.ErrorMessage, item.ExpectedErrorMessageContains) {
        		panic(fmt.Sprintf("%s: %s", item.Name, result.ErrorMessage))
        	}
        }

        func contains(value, fragment string) bool {
        	for index := 0; index+len(fragment) <= len(value); index++ {
        		if value[index:index+len(fragment)] == fragment { return true }
        	}
        	return false
        }
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
            "agents", "audio", "client-tools", "collapsing", "content", "evaluations",
            "executions", "forking", "goals", "interactions", "json",
            "middleware", "multimodal-content", "provider-authentication",
            "runtime-extensions", "server-sent-events", "sessions", "threads",
            "unknown-events"
        };
        return ir.Capabilities
            .Select(static capability => capability.Name)
            .Where(supported.Contains)
            .OrderBy(static capability => capability, StringComparer.Ordinal)
            .ToArray();
    }

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
        return builder.Length == 0 ? "Value" : builder.ToString().Trim('_');
    }

    private static string GoQuote(string value) => JsonSerializer.Serialize(value);

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !char.IsAsciiLetter(value[0]) && value[0] != '_' ||
            value.Skip(1).Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException($"Go package name '{value}' is invalid.");
        }
    }

    private sealed record GoParameter(
        string WireName,
        string Name,
        string Type,
        string Location);

    private const string RuntimeTemplate =
        """
        package __PACKAGE__

        import (
        	"bufio"
        	"bytes"
        	"context"
        	"encoding/json"
        	"fmt"
        	"io"
        	"net/http"
        	"net/url"
        	"sort"
        	"strconv"
        	"strings"
        	"sync"
        	"time"
        )

        type ClientOptions struct {
        	HTTPClient *http.Client
        	Headers    map[string]string
        	MaxRetries int
        }

        type HpdApiError struct {
        	Status  int
        	Code    string
        	Message string
        	Problem map[string]any
        	Errors  map[string][]string
        }

        func (e *HpdApiError) Error() string { return e.Message }

        type Transport struct {
        	baseURL    *url.URL
        	httpClient *http.Client
        	headers    map[string]string
        	maxRetries int
        }

        func NewTransport(baseURL string, options ClientOptions) *Transport {
        	parsed, err := url.Parse(strings.TrimRight(baseURL, "/") + "/")
        	if err != nil { panic(err) }
        	client := options.HTTPClient
        	if client == nil { client = &http.Client{Timeout: 60 * time.Second} }
        	retries := options.MaxRetries
        	if retries < 0 { retries = 0 }
        	headers := map[string]string{"Accept": "application/json"}
        	for key, value := range options.Headers { headers[key] = value }
        	return &Transport{baseURL: parsed, httpClient: client, headers: headers, maxRetries: retries}
        }

        func (t *Transport) RequestJSON(ctx context.Context, method, path string, query url.Values, body any, target any, replaySafe bool) error {
        	payload, err := t.request(ctx, method, path, query, body, replaySafe, "application/json")
        	if err != nil { return err }
        	if len(payload) == 0 { return nil }
        	return json.Unmarshal(payload, target)
        }

        func (t *Transport) RequestNoContent(ctx context.Context, method, path string, query url.Values, body any, replaySafe bool) error {
        	_, err := t.request(ctx, method, path, query, body, replaySafe, "application/json")
        	return err
        }

        func (t *Transport) RequestBytes(ctx context.Context, method, path string, query url.Values, body any, replaySafe bool) ([]byte, error) {
        	return t.request(ctx, method, path, query, body, replaySafe, "application/octet-stream")
        }

        func (t *Transport) request(ctx context.Context, method, path string, query url.Values, body any, replaySafe bool, accept string) ([]byte, error) {
        	relative, _ := url.Parse(strings.TrimLeft(path, "/"))
        	endpoint := t.baseURL.ResolveReference(relative)
        	if len(query) > 0 { endpoint.RawQuery = query.Encode() }
        	var encoded []byte
        	var err error
        	if body != nil { encoded, err = json.Marshal(body); if err != nil { return nil, err } }
        	idempotent := method == http.MethodGet || method == http.MethodHead || method == http.MethodOptions || method == http.MethodPut || method == http.MethodDelete
        	attempts := 1
        	if idempotent || replaySafe { attempts = t.maxRetries + 1 }
        	for attempt := 0; attempt < attempts; attempt++ {
        		request, requestErr := http.NewRequestWithContext(ctx, method, endpoint.String(), bytes.NewReader(encoded))
        		if requestErr != nil { return nil, requestErr }
        		for key, value := range t.headers { request.Header.Set(key, value) }
        		request.Header.Set("Accept", accept)
        		if body != nil { request.Header.Set("Content-Type", "application/json") }
        		response, responseErr := t.httpClient.Do(request)
        		if responseErr != nil {
        			if attempt+1 < attempts { time.Sleep(time.Duration(250*(1<<attempt)) * time.Millisecond); continue }
        			return nil, &HpdApiError{Code: "transport-error", Message: responseErr.Error()}
        		}
        		payload, readErr := io.ReadAll(response.Body)
        		response.Body.Close()
        		if readErr != nil { return nil, readErr }
        		if response.StatusCode >= 200 && response.StatusCode < 300 { return payload, nil }
        		if attempt+1 < attempts && retryableStatus(response.StatusCode) {
        			delay := retryDelay(response.Header.Get("Retry-After"), attempt)
        			time.Sleep(delay)
        			continue
        		}
        		return nil, parseApiError(response.StatusCode, payload)
        	}
        	return nil, &HpdApiError{Code: "retry-exhausted", Message: "request failed"}
        }

        func retryableStatus(status int) bool {
        	return status == http.StatusConflict || status == http.StatusTooManyRequests || status == http.StatusBadGateway || status == http.StatusServiceUnavailable || status == http.StatusGatewayTimeout
        }

        func retryDelay(value string, attempt int) time.Duration {
        	if seconds, err := strconv.Atoi(value); err == nil { return time.Duration(seconds) * time.Second }
        	delay := time.Duration(250*(1<<attempt)) * time.Millisecond
        	if delay > 4*time.Second { return 4 * time.Second }
        	return delay
        }

        func parseApiError(status int, payload []byte) error {
        	var problem map[string]any
        	_ = json.Unmarshal(payload, &problem)
        	code, _ := problem["type"].(string)
        	if code == "" { code = fmt.Sprintf("http-%d", status) }
        	message, _ := problem["detail"].(string)
        	if message == "" { message, _ = problem["title"].(string) }
        	if message == "" { message = string(payload) }
        	errors := map[string][]string{}
        	if rawErrors, ok := problem["errors"].(map[string]any); ok {
        		for field, raw := range rawErrors {
        			if values, ok := raw.([]any); ok {
        				for _, value := range values { if text, ok := value.(string); ok { errors[field] = append(errors[field], text) } }
        			}
        		}
        	}
        	return &HpdApiError{Status: status, Code: code, Message: message, Problem: problem, Errors: errors}
        }

        type ClientToolInvocation struct {
        	RequestID string
        	ToolName string
        	CallID string
        	Arguments map[string]any
        }

        type ClientToolResult struct {
        	Outcome string
        	Content []map[string]any
        	ErrorMessage string
        }

        func ClientToolText(text string) ClientToolResult {
        	return ClientToolResult{Outcome: "Completed", Content: []map[string]any{{"type": "text", "text": text}}}
        }

        func ClientToolFailed(message string) ClientToolResult {
        	return ClientToolResult{Outcome: "Failed", ErrorMessage: message}
        }

        type ClientToolHandler func(invocation ClientToolInvocation) ClientToolResult

        type ClientToolRegistry struct {
        	mu sync.RWMutex
        	handlers map[string]ClientToolHandler
        	fallback ClientToolHandler
        }

        func NewClientToolRegistry() *ClientToolRegistry {
        	return &ClientToolRegistry{handlers: map[string]ClientToolHandler{}}
        }

        func normalizeClientToolName(value string) string {
        	if index := strings.LastIndex(value, "."); index >= 0 && index+1 < len(value) {
        		return value[index+1:]
        	}
        	return strings.TrimSpace(value)
        }

        func (r *ClientToolRegistry) Register(name string, handler ClientToolHandler) *ClientToolRegistry {
        	r.mu.Lock()
        	defer r.mu.Unlock()
        	r.handlers[normalizeClientToolName(name)] = handler
        	return r
        }

        func (r *ClientToolRegistry) RegisterFallback(handler ClientToolHandler) *ClientToolRegistry {
        	r.mu.Lock()
        	defer r.mu.Unlock()
        	r.fallback = handler
        	return r
        }

        func (r *ClientToolRegistry) CanHandle(name string) bool {
        	r.mu.RLock()
        	defer r.mu.RUnlock()
        	_, ok := r.handlers[normalizeClientToolName(name)]
        	return ok || r.fallback != nil
        }

        func (r *ClientToolRegistry) Capabilities() []string {
        	r.mu.RLock()
        	defer r.mu.RUnlock()
        	result := make([]string, 0, len(r.handlers))
        	for name := range r.handlers { result = append(result, "client-tool:"+name) }
        	sort.Strings(result)
        	return result
        }

        func (r *ClientToolRegistry) Invoke(invocation ClientToolInvocation) (result ClientToolResult) {
        	r.mu.RLock()
        	handler := r.handlers[normalizeClientToolName(invocation.ToolName)]
        	if handler == nil { handler = r.fallback }
        	r.mu.RUnlock()
        	if handler == nil { return ClientToolFailed("Unknown client tool: " + invocation.ToolName) }
        	defer func() {
        		if recovered := recover(); recovered != nil {
        			result = ClientToolFailed(fmt.Sprint(recovered))
        		}
        	}()
        	return handler(invocation)
        }

        type HpdSseAppliedCursor struct { Generation int; Sequence int64 }

        func (c HpdSseAppliedCursor) String() string { return fmt.Sprintf("%d:%d", c.Generation, c.Sequence) }

        type HpdSseMessage struct { Event string; ID *string; Data json.RawMessage }

        type HpdStreamError struct { Message string }
        func (e *HpdStreamError) Error() string { return e.Message }

        type HpdJournalRebasedError struct { PreviousGeneration int; CurrentGeneration int }
        func (e *HpdJournalRebasedError) Error() string {
        	return fmt.Sprintf("journal generation changed from %d to %d", e.PreviousGeneration, e.CurrentGeneration)
        }

        type HpdSseState struct {
        	mu sync.Mutex
        	cursor HpdSseAppliedCursor
        }

        func NewHpdSseState(cursor HpdSseAppliedCursor) *HpdSseState { return &HpdSseState{cursor: cursor} }
        func (s *HpdSseState) Cursor() HpdSseAppliedCursor { s.mu.Lock(); defer s.mu.Unlock(); return s.cursor }

        func (s *HpdSseState) Apply(message HpdSseMessage, sequence *int64) (string, error) {
        	s.mu.Lock()
        	defer s.mu.Unlock()
        	if message.Event == "thread-journal-rebased" {
        		var data struct { PreviousGeneration int `json:"previousGeneration"`; CurrentGeneration int `json:"currentGeneration"` }
        		if err := json.Unmarshal(message.Data, &data); err != nil { return "", err }
        		s.cursor = HpdSseAppliedCursor{Generation: data.CurrentGeneration}
        		return "", &HpdJournalRebasedError{PreviousGeneration: data.PreviousGeneration, CurrentGeneration: data.CurrentGeneration}
        	}
        	if message.ID == nil { return "live", nil }
        	parts := strings.SplitN(*message.ID, ":", 2)
        	if len(parts) != 2 { return "", &HpdStreamError{Message: "invalid event cursor " + *message.ID} }
        	generation, err := strconv.Atoi(parts[0]); if err != nil { return "", err }
        	cursorSequence, err := strconv.ParseInt(parts[1], 10, 64); if err != nil { return "", err }
        	if sequence != nil && *sequence != cursorSequence {
        		return "", &HpdStreamError{Message: fmt.Sprintf("event sequence %d did not match cursor %s", *sequence, *message.ID)}
        	}
        	incoming := HpdSseAppliedCursor{Generation: generation, Sequence: cursorSequence}
        	if incoming == s.cursor { return "duplicate", nil }
        	if incoming.Generation != s.cursor.Generation {
        		return "", &HpdStreamError{Message: fmt.Sprintf("cursor generation %d did not match %d", incoming.Generation, s.cursor.Generation)}
        	}
        	if incoming.Sequence <= s.cursor.Sequence { return "duplicate", nil }
        	s.cursor = incoming
        	return "committed", nil
        }

        func (t *Transport) StreamSSE(ctx context.Context, method, path string, query url.Values, lastEventID string) (<-chan HpdSseMessage, error) {
        	relative, _ := url.Parse(strings.TrimLeft(path, "/"))
        	endpoint := t.baseURL.ResolveReference(relative)
        	if len(query) > 0 { endpoint.RawQuery = query.Encode() }
        	request, err := http.NewRequestWithContext(ctx, method, endpoint.String(), nil)
        	if err != nil { return nil, err }
        	for key, value := range t.headers { request.Header.Set(key, value) }
        	request.Header.Set("Accept", "text/event-stream")
        	if lastEventID != "" { request.Header.Set("Last-Event-ID", lastEventID) }
        	response, err := t.httpClient.Do(request)
        	if err != nil { return nil, err }
        	if response.StatusCode < 200 || response.StatusCode >= 300 {
        		defer response.Body.Close()
        		payload, _ := io.ReadAll(response.Body)
        		return nil, parseApiError(response.StatusCode, payload)
        	}
        	messages := make(chan HpdSseMessage)
        	go func() {
        		defer close(messages)
        		defer response.Body.Close()
        		scanner := bufio.NewScanner(response.Body)
        		eventName, eventID := "", ""
        		var data []string
        		flush := func() bool {
        			if len(data) == 0 { return true }
        			raw := []byte(strings.Join(data, "\n"))
        			if json.Valid(raw) {
        				var id *string
        				if eventID != "" { value := eventID; id = &value }
        				select {
        				case messages <- HpdSseMessage{Event: eventName, ID: id, Data: raw}:
        				case <-ctx.Done():
        					return false
        				}
        			}
        			eventName, eventID, data = "", "", nil
        			return true
        		}
        		for scanner.Scan() {
        			line := scanner.Text()
        			if line == "" { if !flush() { return }; continue }
        			if strings.HasPrefix(line, ":") { continue }
        			field, value, _ := strings.Cut(line, ":")
        			value = strings.TrimPrefix(value, " ")
        			switch field {
        			case "event": eventName = value
        			case "id": eventID = value
        			case "data": data = append(data, value)
        			}
        		}
        		flush()
        	}()
        	return messages, nil
        }
        """;
}
