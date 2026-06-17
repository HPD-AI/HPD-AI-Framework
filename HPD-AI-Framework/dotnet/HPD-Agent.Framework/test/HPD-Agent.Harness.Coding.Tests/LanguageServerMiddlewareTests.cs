using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class LanguageServerMiddlewareTests
{
    [Fact]
    public async Task AfterFunctionAsync_DoesNothingWhenDisabled()
    {
        var service = new FakeLanguageServerService { HasServer = true };
        var middleware = new CodingLanguageServerMiddleware(
            new LanguageServerOptions { Enabled = false },
            service);
        var context = CreateAfterFunctionContext(CreateAgentContext(), CreateReadFileSnapshot("/tmp/A.cs"));

        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        service.OpenRequests.Should().BeEmpty();
        context.GetMiddlewareState<LanguageServerState>().Should().BeNull();
    }

    [Fact]
    public async Task AfterFunctionAsync_DoesNotFailWhenNoServerIsAvailable()
    {
        var service = new FakeLanguageServerService { HasServer = false };
        var middleware = new CodingLanguageServerMiddleware(new LanguageServerOptions(), service);
        var context = CreateAfterFunctionContext(CreateAgentContext(), CreateReadFileSnapshot("/tmp/A.cs"));

        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        service.OpenRequests.Should().BeEmpty();
        context.Result.Should().Be("<file />");
    }

    [Fact]
    public async Task AfterFunctionAsync_OpensDocumentAfterSuccessfulReadFile()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "A.cs");
        await File.WriteAllTextAsync(path, "class A {}\n");

        try
        {
            var service = new FakeLanguageServerService { HasServer = true, Opened = true };
            var middleware = new CodingLanguageServerMiddleware(new LanguageServerOptions(), service);
            var agentContext = CreateAgentContext();
            var context = CreateAfterFunctionContext(agentContext, CreateReadFileSnapshot(path));

            await middleware.AfterFunctionAsync(context, CancellationToken.None);

            service.OpenRequests.Should().ContainSingle();
            service.OpenRequests[0].Path.Should().Be(path);
            service.OpenRequests[0].LanguageId.Should().Be("csharp");
            service.OpenRequests[0].Version.Should().Be(0);
            service.OpenRequests[0].Text.Should().Be("class A {}\n");

            var state = context.GetMiddlewareState<LanguageServerState>();
            state.Should().NotBeNull();
            state!.DocumentsByPath[path].Opened.Should().BeTrue();
            state.DocumentsByPath[path].Version.Should().Be(0);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AfterFunctionAsync_DoesNotOpenDocumentWhenReadFileFailed()
    {
        var service = new FakeLanguageServerService { HasServer = true };
        var middleware = new CodingLanguageServerMiddleware(new LanguageServerOptions(), service);
        var context = CreateAfterFunctionContext(CreateAgentContext(), snapshot: null);

        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        service.OpenRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AfterFunctionAsync_AppendsDiagnosticsToMutationResult()
    {
        var agentContext = CreateAgentContext();
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "A.cs");
        await File.WriteAllTextAsync(path, "class A { }\n");

        var diagnosticSet = new LanguageServerDiagnosticSet
        {
            Path = path,
            ServerId = "csharp",
            Source = LanguageServerDiagnosticSource.Publish,
            ReceivedAt = DateTimeOffset.UtcNow,
            Diagnostics =
            [
                new LanguageServerDiagnostic
                {
                    Severity = LanguageServerDiagnosticSeverity.Error,
                    Line = 11,
                    Character = 17,
                    Code = "CS1002",
                    Message = "<missing ;>"
                }
            ]
        };

        try
        {
            var service = new FakeLanguageServerService
            {
                HasServer = true,
                Diagnostics = [diagnosticSet]
            };
            var middleware = new CodingLanguageServerMiddleware(new LanguageServerOptions(), service);
            var context = CreateMutationAfterFunctionContext(
                agentContext,
                new CodingFileMutationSnapshot
                {
                    ToolName = "EditFile",
                    Path = path,
                    Kind = CodingFileMutationKind.Changed,
                    Text = "class A { }\n",
                    LastWriteTimeUtc = DateTimeOffset.UtcNow
                });

            await middleware.AfterFunctionAsync(context, CancellationToken.None);

            context.Result.Should().BeOfType<string>()
                .Which.Should().Contain("<edit_result />")
                .And.Contain("<language_server_diagnostics")
                .And.Contain("source=\"EditFile\"")
                .And.Contain("line=\"12\"")
                .And.Contain("character=\"18\"")
                .And.Contain("&lt;missing ;&gt;");

            service.OpenRequests.Should().ContainSingle();
            service.SaveRequests.Should().ContainSingle();
            service.DiagnosticRequests.Should().ContainSingle()
                .Which.Should().Match<LanguageServerDiagnosticRequest>(request =>
                    request.Mode == LanguageServerDiagnosticMode.Document &&
                    request.DocumentVersion == 0 &&
                    request.StartedAt > DateTimeOffset.MinValue);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AfterFunctionAsync_SendsWatchedFileChangeAndEmitsEventsForMutation()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "A.cs");
        await File.WriteAllTextAsync(path, "class A { }\n");
        var coordinator = new CapturingEventCoordinator();
        var agentContext = CreateAgentContext(coordinator);
        var service = new FakeLanguageServerService
        {
            HasServer = true,
            Opened = true,
            Diagnostics =
            [
                new LanguageServerDiagnosticSet
                {
                    Path = path,
                    ServerId = "csharp",
                    Source = LanguageServerDiagnosticSource.DocumentPull,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    Diagnostics =
                    [
                        new LanguageServerDiagnostic
                        {
                            Severity = LanguageServerDiagnosticSeverity.Error,
                            Line = 3,
                            Character = 7,
                            Code = "CS1002",
                            Message = "Missing semicolon"
                        },
                        new LanguageServerDiagnostic
                        {
                            Severity = LanguageServerDiagnosticSeverity.Information,
                            Line = 5,
                            Character = 1,
                            Message = "Analyzer information"
                        }
                    ]
                }
            ]
        };
        var middleware = new CodingLanguageServerMiddleware(new LanguageServerOptions(), service);
        var context = CreateMutationAfterFunctionContext(
            agentContext,
            new CodingFileMutationSnapshot
            {
                ToolName = "EditFile",
                Path = path,
                Kind = CodingFileMutationKind.Changed,
                Text = "class A { }\n",
                LastWriteTimeUtc = DateTimeOffset.UtcNow
            });

        try
        {
            await middleware.AfterFunctionAsync(context, CancellationToken.None);

            service.WatchedFileChangeRequests.Should().ContainSingle()
                .Which.Should().Match<LanguageServerWatchedFileChangeRequest>(request =>
                    request.Path == Path.GetFullPath(path, Directory.GetCurrentDirectory()) &&
                    request.Kind == LanguageServerWatchedFileChangeKind.Changed);
            coordinator.Captured.OfType<LanguageServerWatchedFileChangedEvent>()
                .Should().ContainSingle(evt => evt.ChangeKind == LanguageServerWatchedFileChangeKind.Changed);
            coordinator.Captured.OfType<LanguageServerDiagnosticsReceivedEvent>()
                .Should().ContainSingle()
                .Which.Should().Match<LanguageServerDiagnosticsReceivedEvent>(evt =>
                    evt.ErrorCount == 1 &&
                    evt.WarningCount == 0 &&
                    evt.InformationCount == 1 &&
                    evt.HintCount == 0 &&
                    evt.DiagnosticSetCount == 1 &&
                    evt.Diagnostics.Count == 2 &&
                    !evt.DiagnosticsTruncated &&
                    evt.Diagnostics[0].ServerId == "csharp" &&
                    evt.Diagnostics[0].Severity == LanguageServerDiagnosticSeverity.Error &&
                    evt.Diagnostics[0].Line == 3 &&
                    evt.Diagnostics[0].Character == 7 &&
                    evt.Diagnostics[0].Code == "CS1002" &&
                    evt.Diagnostics[0].Message == "Missing semicolon");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AfterFunctionAsync_DeletedMutationNotifiesClosesAndClearsState()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.GetFullPath(Path.Combine(tempRoot, "A.cs"), Directory.GetCurrentDirectory());
        var uri = new Uri(path).AbsoluteUri;
        await File.WriteAllTextAsync(path, "class A { }\n");
        var service = new FakeLanguageServerService { HasServer = true, Opened = true };
        var middleware = new CodingLanguageServerMiddleware(new LanguageServerOptions(), service);
        var context = CreateMutationAfterFunctionContext(
            CreateAgentContext(),
            new CodingFileMutationSnapshot
            {
                ToolName = "WriteFile",
                Path = path,
                Kind = CodingFileMutationKind.Deleted
            });
        context.UpdateMiddlewareState<LanguageServerState>(state => state with
        {
            DocumentsByPath = new Dictionary<string, LanguageServerDocumentSnapshot>(StringComparer.Ordinal)
            {
                [path] = new()
                {
                    Path = path,
                    Uri = uri,
                    LanguageId = "csharp",
                    Version = 3,
                    Opened = true,
                    LastObservedAt = DateTimeOffset.UtcNow
                }
            },
            DiagnosticsByPath = new Dictionary<string, LanguageServerDiagnosticSet>(StringComparer.Ordinal)
            {
                ["csharp\u001f" + path] = CreateDiagnosticSet(path, "csharp")
            }
        });

        try
        {
            await middleware.AfterFunctionAsync(context, CancellationToken.None);

            service.WatchedFileChangeRequests.Should().ContainSingle()
                .Which.Kind.Should().Be(LanguageServerWatchedFileChangeKind.Deleted);
            service.CloseRequests.Should().ContainSingle()
                .Which.Uri.Should().Be(uri);
            service.DiagnosticRequests.Should().BeEmpty();

            var state = context.GetMiddlewareState<LanguageServerState>();
            state.Should().NotBeNull();
            state!.DocumentsByPath.Should().NotContainKey(path);
            state.DiagnosticsByPath.Values.Should().NotContain(set => set.Path == path);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BeforeIterationAsync_InjectsPendingDiagnosticsOnce()
    {
        var agentContext = CreateAgentContext();
        var path = "/repo/A.cs";
        var state = new LanguageServerState
        {
            PendingFeedback =
            [
                new LanguageServerPendingFeedback
                {
                    Id = "csharp|/repo/A.cs|1|result",
                    CreatedAt = DateTimeOffset.UtcNow,
                    DiagnosticSet = new LanguageServerDiagnosticSet
                    {
                        Path = path,
                        ServerId = "csharp",
                        Source = LanguageServerDiagnosticSource.Publish,
                        Version = 1,
                        ResultId = "result",
                        ReceivedAt = DateTimeOffset.UtcNow,
                        Diagnostics =
                        [
                            new LanguageServerDiagnostic
                            {
                                Severity = LanguageServerDiagnosticSeverity.Error,
                                Line = 0,
                                Character = 2,
                                Code = "CS1002",
                                Message = "expected ;"
                            }
                        ]
                    }
                }
            ]
        };

        var middleware = new CodingLanguageServerMiddleware(
            new LanguageServerOptions(),
            new FakeLanguageServerService());

        var first = CreateBeforeIterationContext(agentContext, []);
        first.UpdateMiddlewareState<LanguageServerState>(_ => state);
        await middleware.BeforeIterationAsync(first, CancellationToken.None);

        first.Options.Instructions.Should().Contain("<language_server_feedback>");
        first.Options.Instructions.Should().Contain("line=\"1\"");
        first.Options.Instructions.Should().Contain("character=\"3\"");

        var updated = first.GetMiddlewareState<LanguageServerState>();
        updated.Should().NotBeNull();
        updated!.PendingFeedback.Should().ContainSingle().Which.Injected.Should().BeTrue();

        var second = CreateBeforeIterationContext(agentContext, []);
        await middleware.BeforeIterationAsync(second, CancellationToken.None);

        second.Options.Instructions.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunctionAsync_RecordsObservedCodingToolIntent()
    {
        var agentContext = CreateAgentContext();
        var middleware = new CodingLanguageServerMiddleware(
            new LanguageServerOptions(),
            new FakeLanguageServerService());
        var function = AIFunctionFactory.Create(() => "ok", "ReadFile");
        var context = agentContext.AsBeforeFunction(
            function,
            "call-read",
            new Dictionary<string, object?> { ["path"] = "/repo/A.cs" },
            new AgentRunConfig(),
            toolharnessName: nameof(CodingToolHarness));

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        var state = context.GetMiddlewareState<LanguageServerState>();
        state.Should().NotBeNull();
        state!.PendingOperations.Should().ContainSingle(operation =>
            operation.CallId == "call-read" &&
            operation.ToolName == "ReadFile" &&
            operation.Path == "/repo/A.cs");
    }

    [Fact]
    public async Task AfterMessageTurnAsync_ClearsInjectedFeedbackAndPendingOperations()
    {
        var agentContext = CreateAgentContext();
        var state = new LanguageServerState
        {
            PendingOperations =
            [
                new LanguageServerPendingOperation
                {
                    CallId = "call-read",
                    ToolName = "ReadFile",
                    Path = "/repo/A.cs",
                    ObservedAt = DateTimeOffset.UtcNow
                }
            ],
            PendingFeedback =
            [
                new LanguageServerPendingFeedback
                {
                    Id = "injected",
                    Injected = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    DiagnosticSet = CreateDiagnosticSet("/repo/A.cs", "csharp")
                },
                new LanguageServerPendingFeedback
                {
                    Id = "waiting",
                    CreatedAt = DateTimeOffset.UtcNow,
                    DiagnosticSet = CreateDiagnosticSet("/repo/B.cs", "csharp")
                }
            ]
        };
        var context = agentContext.AsAfterMessageTurn(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")),
            [],
            new AgentRunConfig());
        context.UpdateMiddlewareState<LanguageServerState>(_ => state);

        await new CodingLanguageServerMiddleware(
            new LanguageServerOptions(),
            new FakeLanguageServerService()).AfterMessageTurnAsync(context, CancellationToken.None);

        var updated = context.GetMiddlewareState<LanguageServerState>();
        updated.Should().NotBeNull();
        updated!.PendingOperations.Should().BeEmpty();
        updated.PendingFeedback.Should().ContainSingle(feedback => feedback.Id == "waiting");
    }

    [Fact]
    public async Task AfterFunctionAsync_RecordsUnavailableServersInMiddlewareState()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "A.cs");
        await File.WriteAllTextAsync(path, "class A {}\n");
        var service = new FakeLanguageServerService
        {
            HasServer = true,
            Statuses =
            [
                new LanguageServerStatus
                {
                    ServerId = "csharp",
                    Root = "/repo",
                    Status = LanguageServerStatusKind.Unavailable,
                    Message = "not installed"
                }
            ]
        };
        var middleware = new CodingLanguageServerMiddleware(
            new LanguageServerOptions { ConfigVersion = 7 },
            service);
        var context = CreateAfterFunctionContext(CreateAgentContext(), CreateReadFileSnapshot(path));

        try
        {
            await middleware.AfterFunctionAsync(context, CancellationToken.None);

            var state = context.GetMiddlewareState<LanguageServerState>();
            state.Should().NotBeNull();
            state!.UnavailableServers.Values.Should().ContainSingle(server =>
                server.ServerId == "csharp" &&
                server.Root == "/repo" &&
                server.ConfigVersion == 7 &&
                server.Reason == "not installed");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EnvironmentMiddleware_RecordsMutationSnapshotInReadFileState()
    {
        var agentContext = CreateAgentContext();
        var path = Path.Combine(Path.GetTempPath(), $"hpd-mutation-{Guid.NewGuid():N}.cs");
        var mutation = new CodingFileMutationSnapshot
        {
            ToolName = "WriteFile",
            Path = path,
            Kind = CodingFileMutationKind.Created,
            Text = "class A\n{\n}\n",
            LastWriteTimeUtc = DateTimeOffset.UtcNow
        };
        var context = CreateMutationAfterFunctionContext(agentContext, mutation);

        await new EnvironmentContextMiddleware().AfterFunctionAsync(context, CancellationToken.None);

        var state = context.GetMiddlewareState<ReadFileState>();
        state.Should().NotBeNull();
        state!.FilesByPath[path].Coverage.Should().Be(ReadFileCoverage.FullFile);
        state.FilesByPath[path].TotalLines.Should().Be(3);
        state.FilesByPath[path].SourceKind.Should().Be(ReadFileSourceKind.FileSystem);
    }

    [Fact]
    public async Task EnvironmentMiddleware_UsesMutationByteLengthWhenProvided()
    {
        var agentContext = CreateAgentContext();
        var path = Path.Combine(Path.GetTempPath(), $"hpd-mutation-{Guid.NewGuid():N}.txt");
        var mutation = new CodingFileMutationSnapshot
        {
            ToolName = "EditFile",
            Path = path,
            Kind = CodingFileMutationKind.Changed,
            Text = "hello",
            ByteLength = 42,
            LastWriteTimeUtc = DateTimeOffset.UtcNow
        };
        var context = CreateMutationAfterFunctionContext(agentContext, mutation);

        await new EnvironmentContextMiddleware().AfterFunctionAsync(context, CancellationToken.None);

        var state = context.GetMiddlewareState<ReadFileState>();
        state.Should().NotBeNull();
        state!.FilesByPath[path].Length.Should().Be(42);
    }

    [Fact]
    public async Task EnvironmentMiddleware_DeletedMutationRemovesReadFileState()
    {
        var agentContext = CreateAgentContext();
        var path = Path.Combine(Path.GetTempPath(), $"hpd-mutation-{Guid.NewGuid():N}.cs");
        var middleware = new EnvironmentContextMiddleware();

        await middleware.AfterFunctionAsync(
            CreateAfterFunctionContext(agentContext, CreateReadFileSnapshot(path)),
            CancellationToken.None);

        var deleteContext = CreateMutationAfterFunctionContext(
            agentContext,
            new CodingFileMutationSnapshot
            {
                ToolName = "WriteFile",
                Path = path,
                Kind = CodingFileMutationKind.Deleted
            });

        await middleware.AfterFunctionAsync(deleteContext, CancellationToken.None);

        var state = deleteContext.GetMiddlewareState<ReadFileState>();
        state.Should().NotBeNull();
        state!.FilesByPath.Should().NotContainKey(path);
    }

    [Fact]
    public void Formatter_HidesWarningsByDefaultAndEscapesXml()
    {
        var formatter = new LanguageServerDiagnosticFormatter();
        var xml = formatter.FormatMutationDiagnostics(
            "/repo/A.cs",
            "EditFile",
            [
                new LanguageServerDiagnosticSet
                {
                    Path = "/repo/A.cs",
                    ServerId = "csharp",
                    Source = LanguageServerDiagnosticSource.Publish,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    Diagnostics =
                    [
                        new LanguageServerDiagnostic
                        {
                            Severity = LanguageServerDiagnosticSeverity.Error,
                            Line = 0,
                            Character = 1,
                            Code = "CS",
                            Message = "bad <xml> & value"
                        },
                        new LanguageServerDiagnostic
                        {
                            Severity = LanguageServerDiagnosticSeverity.Warning,
                            Line = 1,
                            Character = 1,
                            Message = "hidden"
                        }
                    ]
                }
            ],
            new LanguageServerFeedbackOptions());

        xml.Should().Contain("<language_server_diagnostics");
        xml.Should().Contain("bad &lt;xml&gt; &amp; value");
        xml.Should().Contain("line=\"1\"");
        xml.Should().Contain("character=\"2\"");
        xml.Should().NotContain("hidden");
    }

    [Fact]
    public void ProtocolClient_CurrentDiagnosticsMergesPushAndPullWithoutDuplicateDiagnostics()
    {
        var client = CreateProtocolClient();
        var path = Path.Combine(Path.GetTempPath(), "A.ts");
        var uri = new Uri(path).AbsoluteUri;

        client.AcceptPublishedDiagnosticsForTesting(new JsonObject
        {
            ["uri"] = uri,
            ["version"] = 1,
            ["diagnostics"] = new JsonArray
            {
                CreateLspDiagnostic(1, 2, "2322", "Type mismatch")
            }
        });

        client.ParseDocumentDiagnosticReportForTesting(
            path,
            uri,
            new JsonObject
            {
                ["kind"] = "full",
                ["items"] = new JsonArray
                {
                    CreateLspDiagnostic(1, 2, "2322", "Type mismatch")
                }
            });

        client.CurrentDiagnostics
            .SelectMany(set => set.Diagnostics)
            .Should().ContainSingle()
            .Which.Message.Should().Be("Type mismatch");
    }

    [Fact]
    public void ProtocolClient_EmptyDocumentPullClearsPreviousPullDiagnostics()
    {
        var client = CreateProtocolClient();
        var path = Path.Combine(Path.GetTempPath(), "A.ts");
        var uri = new Uri(path).AbsoluteUri;

        client.ParseDocumentDiagnosticReportForTesting(
            path,
            uri,
            new JsonObject
            {
                ["kind"] = "full",
                ["items"] = new JsonArray
                {
                    CreateLspDiagnostic(0, 0, "2322", "old")
                }
            });

        client.ParseDocumentDiagnosticReportForTesting(
            path,
            uri,
            new JsonObject
            {
                ["kind"] = "full",
                ["items"] = new JsonArray()
            });

        client.CurrentDiagnostics.Should().ContainSingle(set => set.Path == path)
            .Which.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ProtocolClient_UnchangedDocumentPullReusesPreviousDiagnosticsAndUpdatesResultId()
    {
        var client = CreateProtocolClient();
        var path = Path.Combine(Path.GetTempPath(), "A.ts");
        var uri = new Uri(path).AbsoluteUri;

        client.ParseDocumentDiagnosticReportForTesting(
            path,
            uri,
            new JsonObject
            {
                ["kind"] = "full",
                ["resultId"] = "old",
                ["items"] = new JsonArray
                {
                    CreateLspDiagnostic(0, 0, "2322", "old")
                }
            });

        var result = client.ParseDocumentDiagnosticReportForTesting(
            path,
            uri,
            new JsonObject
            {
                ["kind"] = "unchanged",
                ["resultId"] = "new"
            });

        result.MatchedRequestedDocument.Should().BeTrue();
        client.CurrentDiagnostics.Should().ContainSingle(set => set.Path == path)
            .Which.ResultId.Should().Be("new");
        client.CurrentDiagnostics.SelectMany(set => set.Diagnostics)
            .Should().ContainSingle()
            .Which.Message.Should().Be("old");
    }

    [Fact]
    public void ProtocolClient_DocumentPullCapturesRelatedDocuments()
    {
        var client = CreateProtocolClient();
        var path = Path.Combine(Path.GetTempPath(), "A.ts");
        var relatedPath = Path.Combine(Path.GetTempPath(), "B.ts");
        var uri = new Uri(path).AbsoluteUri;
        var relatedUri = new Uri(relatedPath).AbsoluteUri;

        var result = client.ParseDocumentDiagnosticReportForTesting(
            path,
            uri,
            new JsonObject
            {
                ["kind"] = "full",
                ["items"] = new JsonArray(),
                ["relatedDocuments"] = new JsonObject
                {
                    [relatedUri] = new JsonObject
                    {
                        ["kind"] = "full",
                        ["items"] = new JsonArray
                        {
                            CreateLspDiagnostic(3, 4, "7006", "related")
                        }
                    }
                }
            });

        result.MatchedRequestedDocument.Should().BeTrue();
        client.CurrentDiagnostics.Should().Contain(set => set.Path == relatedPath);
        client.CurrentDiagnostics.Single(set => set.Path == relatedPath)
            .Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Be("related");
    }

    [Fact]
    public void ProtocolClient_DynamicDiagnosticRegistrationStoresIdentifierAndWorkspaceMode()
    {
        var client = CreateProtocolClient();

        client.RegisterCapabilityForTesting(new JsonObject
        {
            ["registrations"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "diagnostics-1",
                    ["method"] = "textDocument/diagnostic",
                    ["registerOptions"] = new JsonObject
                    {
                        ["identifier"] = "eslint",
                        ["workspaceDiagnostics"] = true
                    }
                }
            }
        });

        client.DynamicRegistrations.Should().ContainKey("diagnostics-1");
        client.DynamicRegistrations["diagnostics-1"].Identifier.Should().Be("eslint");
        client.DynamicRegistrations["diagnostics-1"].WorkspaceDiagnostics.Should().BeTrue();
    }

    [Fact]
    public void ProtocolClient_DidChangeUsesTextOnlyContentChangeForFullSync()
    {
        var client = CreateProtocolClient();

        var changes = client.CreateDidChangeContentChangesForTesting(
            LanguageServerTextDocumentSyncKind.Full,
            "second\nthird\n",
            "first\n");

        changes.Should().ContainSingle();
        var change = changes[0]!.AsObject();
        change["text"]!.GetValue<string>().Should().Be("second\nthird\n");
        change.ContainsKey("range").Should().BeFalse();
    }

    [Fact]
    public void ProtocolClient_DidChangeUsesWholeDocumentRangeForIncrementalSync()
    {
        var client = CreateProtocolClient();

        var changes = client.CreateDidChangeContentChangesForTesting(
            LanguageServerTextDocumentSyncKind.Incremental,
            "second\nthird\n",
            "first\n");

        changes.Should().ContainSingle();
        var change = changes[0]!.AsObject();
        change["text"]!.GetValue<string>().Should().Be("second\nthird\n");
        var range = change["range"]!.AsObject();
        range["start"]!["line"]!.GetValue<int>().Should().Be(0);
        range["start"]!["character"]!.GetValue<int>().Should().Be(0);
        range["end"]!["line"]!.GetValue<int>().Should().Be(1);
        range["end"]!["character"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void ProtocolClient_DidChangeIncrementalEndPositionUsesUtf16CodeUnits()
    {
        var client = CreateProtocolClient();

        var changes = client.CreateDidChangeContentChangesForTesting(
            LanguageServerTextDocumentSyncKind.Incremental,
            "replacement",
            "emoji: \ud83d\ude00");

        var end = changes[0]!["range"]!["end"]!;
        end["line"]!.GetValue<int>().Should().Be(0);
        end["character"]!.GetValue<int>().Should().Be(9);
    }

    [Fact]
    public async Task LanguageServerService_ResolvesConfiguredServerLanguageAndRoot()
    {
        var tempRoot = CreateTempRoot();
        var projectRoot = Path.Combine(tempRoot, "src");
        Directory.CreateDirectory(projectRoot);
        var path = Path.Combine(projectRoot, "App.tsx");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "package.json"), "{}");
        await File.WriteAllTextAsync(path, "export function App() { return null; }\n");

        try
        {
            var service = new LanguageServerService(new LanguageServerOptions
            {
                WorkspaceFolders = [tempRoot],
                Servers =
                [
                    new LanguageServerDefinition
                    {
                        Id = "typescript",
                        Extensions = [".ts", ".tsx"],
                        LanguageIds = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [".ts"] = "typescript",
                            [".tsx"] = "typescriptreact"
                        },
                        Provider = new StaticCommandLanguageServerProvider(
                            ["package.json"],
                            "typescript-language-server",
                            ["--stdio"])
                    }
                ]
            });

            var resolution = await service.ResolveDocumentAsync(path, CancellationToken.None);

            resolution.HasServers.Should().BeTrue();
            resolution.Path.Should().Be(path);
            resolution.PrimaryLanguageId.Should().Be("typescriptreact");
            resolution.Servers.Should().ContainSingle()
                .Which.Root.Should().Be(projectRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LanguageServerService_UsesWellKnownLanguagesOnlyWhenEnabled()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "Program.cs");
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "App.slnx"), "");
        await File.WriteAllTextAsync(path, "class Program { }\n");

        try
        {
            var disabled = new LanguageServerService(new LanguageServerOptions
            {
                WorkspaceFolders = [tempRoot]
            });

            var enabled = new LanguageServerService(new LanguageServerOptions
            {
                WorkspaceFolders = [tempRoot],
                AllowWellKnownLocalServers = true
            });

            (await disabled.ResolveDocumentAsync(path, CancellationToken.None)).HasServers.Should().BeFalse();

            var resolution = await enabled.ResolveDocumentAsync(path, CancellationToken.None);
            resolution.HasServers.Should().BeTrue();
            resolution.PrimaryLanguageId.Should().Be("csharp");
            resolution.Servers.Should().ContainSingle(server => server.ServerId == "csharp" && server.Root == tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LanguageServerService_UsesGeneratedLanguageServerRegistryProvider()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "App.tsx");
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "package.json"), "{}");
        await File.WriteAllTextAsync(path, "export function App() { return null; }\n");

        try
        {
            var service = new LanguageServerService(new LanguageServerOptions
            {
                WorkspaceFolders = [tempRoot]
            });

            var resolution = await service.ResolveDocumentAsync(path, CancellationToken.None);

            resolution.HasServers.Should().BeTrue();
            resolution.PrimaryLanguageId.Should().Be("typescriptreact");
            resolution.Servers.Should().ContainSingle(server =>
                server.ServerId == "typescript" &&
                server.Root == tempRoot &&
                server.LanguageId == "typescriptreact");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task LanguageServerService_TypeScriptSmoke_StartsRealLanguageServerWhenEnabled()
    {
        if (!string.Equals(
                System.Environment.GetEnvironmentVariable("HPD_LSP_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var smokeRoot = ResolveTypeScriptSmokeRoot();
        smokeRoot.Should().NotBeNull(
            "set HPD_LSP_SMOKE_TYPESCRIPT_ROOT to a TypeScript project with local typescript and typescript-language-server dependencies");

        var dependencyStatus = GetTypeScriptSmokeDependencyStatus(smokeRoot!);
        dependencyStatus.IsReady.Should().BeTrue(dependencyStatus.Message);

        var path = Path.Combine(smokeRoot!, "hpd-lsp-smoke.ts");
        var originalText = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        var uri = new Uri(path).AbsoluteUri;
        await File.WriteAllTextAsync(path, "export const hpdLspSmoke: string = 42;\n");

        await using var service = new LanguageServerService(new LanguageServerOptions
        {
            WorkspaceFolders = [smokeRoot!]
        });

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var resolution = await service.ResolveDocumentAsync(path, timeout.Token);
            resolution.HasServers.Should().BeTrue();
            resolution.PrimaryLanguageId.Should().Be("typescript");
            resolution.Servers.Should().ContainSingle(server => server.ServerId == "typescript");

            var diagnosticStartedAt = DateTimeOffset.UtcNow;
            var open = await service.OpenDocumentAsync(
                new LanguageServerDocumentOpenRequest
                {
                    Path = path,
                    Uri = uri,
                    LanguageId = resolution.PrimaryLanguageId!,
                    Text = await File.ReadAllTextAsync(path, timeout.Token),
                    Version = 0
                },
                timeout.Token);

            open.Opened.Should().BeTrue(await FormatLanguageServerStatusesAsync(service));

            await service.SaveDocumentAsync(
                new LanguageServerDocumentSaveRequest
                {
                    Path = path,
                    Uri = uri,
                    Text = await File.ReadAllTextAsync(path, timeout.Token)
                },
                timeout.Token);

            var diagnostics = await service.GetDiagnosticsAsync(
                new LanguageServerDiagnosticRequest
                {
                    Path = path,
                    Uri = uri,
                    Mode = LanguageServerDiagnosticMode.Document,
                    DocumentVersion = 0,
                    StartedAt = diagnosticStartedAt,
                    Timeout = TimeSpan.FromSeconds(5)
                },
                timeout.Token);

            diagnostics.Should().NotBeNull();
        }
        finally
        {
            if (originalText is null)
                File.Delete(path);
            else
                await File.WriteAllTextAsync(path, originalText);
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task CodingLanguageServerMiddleware_TypeScriptSmoke_WritesReturnedMutationResultWhenEnabled()
    {
        if (!string.Equals(
                System.Environment.GetEnvironmentVariable("HPD_LSP_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var smokeRoot = ResolveTypeScriptSmokeRoot();
        smokeRoot.Should().NotBeNull(
            "set HPD_LSP_SMOKE_TYPESCRIPT_ROOT to a TypeScript project with local typescript and typescript-language-server dependencies");

        var dependencyStatus = GetTypeScriptSmokeDependencyStatus(smokeRoot!);
        dependencyStatus.IsReady.Should().BeTrue(dependencyStatus.Message);

        var path = Path.Combine(smokeRoot!, "hpd-lsp-middleware-smoke.ts");
        var originalText = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        var smokeText = "export const hpdMiddlewareSmoke: string = 42;\n";
        await File.WriteAllTextAsync(path, smokeText);

        var options = new LanguageServerOptions
        {
            WorkspaceFolders = [smokeRoot!],
            Feedback = new LanguageServerFeedbackOptions
            {
                ShowWarnings = true,
                MaxFeedbackCharacters = 12000
            }
        };

        await using var middleware = new CodingLanguageServerMiddleware(options);
        var context = CreateMutationAfterFunctionContext(
            CreateAgentContext(),
            new CodingFileMutationSnapshot
            {
                ToolName = "EditFile",
                Path = path,
                Kind = CodingFileMutationKind.Changed,
                Text = smokeText,
                LastWriteTimeUtc = DateTimeOffset.UtcNow
            });

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await middleware.AfterFunctionAsync(context, timeout.Token);

            var outputPath = Path.Combine(Path.GetTempPath(), "hpd-lsp-middleware-smoke-result.xml");
            await File.WriteAllTextAsync(
                outputPath,
                FormatMiddlewareSmokeOutput(context),
                timeout.Token);

            var state = context.GetMiddlewareState<LanguageServerState>();
            state.Should().NotBeNull();
            state!.DocumentsByPath.Should().ContainKey(Path.GetFullPath(path, Directory.GetCurrentDirectory()));
        }
        finally
        {
            if (originalText is null)
                File.Delete(path);
            else
                await File.WriteAllTextAsync(path, originalText);
        }
    }

    [Fact]
    public async Task LanguageServerService_CachesUnavailableLaunchAttemptByConfigVersion()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "file.fake");
        await File.WriteAllTextAsync(path, "content\n");
        var provider = new CountingNullProvider();
        var definition = new LanguageServerDefinition
        {
            Id = "fake",
            Extensions = [".fake"],
            Provider = provider
        };

        try
        {
            var service = new LanguageServerService(new LanguageServerOptions
            {
                WorkspaceFolders = [tempRoot],
                Servers = [definition],
                ConfigVersion = 1
            });

            var uri = new Uri(path).AbsoluteUri;
            await service.OpenDocumentAsync(
                new LanguageServerDocumentOpenRequest
                {
                    Path = path,
                    Uri = uri,
                    LanguageId = "fake",
                    Text = "content\n"
                },
                CancellationToken.None);

            await service.OpenDocumentAsync(
                new LanguageServerDocumentOpenRequest
                {
                    Path = path,
                    Uri = uri,
                    LanguageId = "fake",
                    Text = "content\n"
                },
                CancellationToken.None);

            provider.ResolveCount.Should().Be(1);
            var status = await service.GetStatusAsync(CancellationToken.None);
            status.Should().ContainSingle(item =>
                item.ServerId == "fake" &&
                item.Root == tempRoot &&
                item.Status == LanguageServerStatusKind.Unavailable);

            var nextVersionProvider = new CountingNullProvider();
            var nextVersionService = new LanguageServerService(new LanguageServerOptions
            {
                WorkspaceFolders = [tempRoot],
                Servers = [definition with { Provider = nextVersionProvider }],
                ConfigVersion = 2
            });

            await nextVersionService.OpenDocumentAsync(
                new LanguageServerDocumentOpenRequest
                {
                    Path = path,
                    Uri = uri,
                    LanguageId = "fake",
                    Text = "content\n"
                },
                CancellationToken.None);

            nextVersionProvider.ResolveCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LanguageServerService_ReturnsAllMatchingDefinitionsForAnExtension()
    {
        var tempRoot = CreateTempRoot();
        var path = Path.Combine(tempRoot, "file.ts");
        await File.WriteAllTextAsync(path, "export const value = 1;\n");
        var typescriptProvider = new CountingNullProvider();
        var eslintProvider = new CountingNullProvider();

        try
        {
            var service = new LanguageServerService(new LanguageServerOptions
            {
                WorkspaceFolders = [tempRoot],
                Servers =
                [
                    new LanguageServerDefinition
                    {
                        Id = "typescript",
                        Extensions = [".ts"],
                        LanguageIds = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [".ts"] = "typescript"
                        },
                        Provider = typescriptProvider
                    },
                    new LanguageServerDefinition
                    {
                        Id = "eslint",
                        Extensions = [".ts"],
                        LanguageIds = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [".ts"] = "typescript"
                        },
                        Provider = eslintProvider
                    }
                ]
            });

            var resolution = await service.ResolveDocumentAsync(path, CancellationToken.None);

            resolution.Servers.Should().HaveCount(2);
            resolution.Servers.Select(server => server.ServerId).Should().BeEquivalentTo("typescript", "eslint");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static AgentContext CreateAgentContext(IEventCoordinator? eventCoordinator = null)
    {
        var state = AgentLoopState.InitialSafe(
            [],
            "test-run",
            "test-conversation",
            "test-agent");

        return new AgentContext(
            "test-agent",
            "test-conversation",
            state,
            eventCoordinator ?? new EventCoordinator(),
            new Session("test-session"),
            new Thread("test-session"),
            CancellationToken.None);
    }

    private static AfterFunctionContext CreateAfterFunctionContext(
        AgentContext agentContext,
        ReadFileSnapshot? snapshot)
    {
        var metadata = new ToolResultMetadata();
        if (snapshot != null)
            metadata.Set(CodingToolMetadataKeys.ReadFileSnapshot, snapshot);

        return agentContext.AsAfterFunction(
            function: null,
            callId: "call-1",
            result: "<file />",
            exception: null,
            runConfig: new AgentRunConfig(),
            toolharnessName: "CodingToolHarness",
            resultMetadata: metadata);
    }

    private static AfterFunctionContext CreateMutationAfterFunctionContext(
        AgentContext agentContext,
        CodingFileMutationSnapshot mutation)
    {
        var metadata = new ToolResultMetadata();
        metadata.Set(CodingToolMetadataKeys.FileMutationSnapshot, mutation);

        return agentContext.AsAfterFunction(
            function: null,
            callId: "call-1",
            result: "<edit_result />",
            exception: null,
            runConfig: new AgentRunConfig(),
            toolharnessName: "CodingToolHarness",
            resultMetadata: metadata);
    }

    private static BeforeIterationContext CreateBeforeIterationContext(
        AgentContext agentContext,
        List<ChatMessage> messages)
        => agentContext.AsBeforeIteration(
            iteration: 0,
            messages,
            new ChatOptions(),
            new AgentRunConfig());

    private static ReadFileSnapshot CreateReadFileSnapshot(string path)
        => new()
        {
            Path = path,
            ReadAt = DateTimeOffset.UtcNow,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            Length = 10,
            Offset = 1,
            Limit = 2000,
            StartLine = 1,
            EndLine = 1,
            LinesRead = 1,
            TotalLines = 1,
            Coverage = ReadFileCoverage.FullFile,
            SourceKind = ReadFileSourceKind.FileSystem,
            ReturnedContentHash = "hash"
        };

    private static LanguageServerDiagnosticSet CreateDiagnosticSet(string path, string serverId)
        => new()
        {
            Path = path,
            ServerId = serverId,
            Source = LanguageServerDiagnosticSource.Publish,
            ReceivedAt = DateTimeOffset.UtcNow,
            Diagnostics =
            [
                new LanguageServerDiagnostic
                {
                    Severity = LanguageServerDiagnosticSeverity.Error,
                    Line = 0,
                    Character = 0,
                    Message = "error"
                }
            ]
        };

    private static LanguageServerProtocolClient CreateProtocolClient()
        => new(
            "typescript",
            Path.GetTempPath(),
            new LanguageServerLaunchDescriptor
            {
                FileName = "typescript-language-server",
                Arguments = ["--stdio"],
                WorkingDirectory = Path.GetTempPath()
            },
            new LanguageServerOptions());

    private static JsonObject CreateLspDiagnostic(int line, int character, string code, string message)
        => new()
        {
            ["severity"] = (int)LanguageServerDiagnosticSeverity.Error,
            ["code"] = code,
            ["message"] = message,
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = line,
                    ["character"] = character
                },
                ["end"] = new JsonObject
                {
                    ["line"] = line,
                    ["character"] = character + 1
                }
            }
        };

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-lsp-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string? ResolveTypeScriptSmokeRoot()
    {
        var configuredRoot = System.Environment.GetEnvironmentVariable("HPD_LSP_SMOKE_TYPESCRIPT_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            return Path.GetFullPath(configuredRoot, Directory.GetCurrentDirectory());

        var candidateRoots = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "HPD-AI-Framework", "typescript", "hpd-agent-acp"),
            Path.Combine(Directory.GetCurrentDirectory(), "HPD-AI-Framework", "typescript", "hpd-agent-client"),
            Path.Combine(Directory.GetCurrentDirectory(), "HPD-AI-Framework", "typescript", "hpd-agent-headless-ui")
        };

        return candidateRoots.FirstOrDefault(HasTypeScriptSmokeDependencies);
    }

    private static bool HasTypeScriptSmokeDependencies(string root)
        => GetTypeScriptSmokeDependencyStatus(root).IsReady;

    private static TypeScriptSmokeDependencyStatus GetTypeScriptSmokeDependencyStatus(string root)
    {
        var tsserverPath = Path.Combine(root, "node_modules", "typescript", "lib", "tsserver.js");
        var localServer = FindLocalExecutable(root, "typescript-language-server");
        var pathServer = FindPathExecutable("typescript-language-server");
        var hasTsserver = File.Exists(tsserverPath);
        var hasServer = localServer is not null || pathServer is not null;

        return new TypeScriptSmokeDependencyStatus(
            hasTsserver && hasServer,
            $"because TypeScript LSP smoke root '{root}' must contain '{tsserverPath}' " +
            $"and must resolve 'typescript-language-server' from '{Path.Combine(root, "node_modules", ".bin")}' or PATH. " +
            $"Found tsserver: {hasTsserver}; found typescript-language-server: {hasServer}.");
    }

    private static string? FindLocalExecutable(string root, string name)
        => FindExecutableInDirectory(Path.Combine(root, "node_modules", ".bin"), name);

    private static string? FindPathExecutable(string name)
    {
        var path = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            var executable = FindExecutableInDirectory(directory, name);
            if (executable is not null)
                return executable;
        }

        return null;
    }

    private static string? FindExecutableInDirectory(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        foreach (var candidateName in GetExecutableCandidateNames(name))
        {
            var candidate = Path.Combine(directory, candidateName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetExecutableCandidateNames(string name)
    {
        yield return name;

        if (!OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Path.GetExtension(name)))
            yield break;

        var pathExt = System.Environment.GetEnvironmentVariable("PATHEXT");
        foreach (var extension in string.IsNullOrWhiteSpace(pathExt)
            ? [".COM", ".EXE", ".BAT", ".CMD"]
            : pathExt.Split(';'))
        {
            if (!string.IsNullOrWhiteSpace(extension))
                yield return name + extension;
        }
    }

    private static async Task<string> FormatLanguageServerStatusesAsync(ILanguageServerService service)
    {
        var statuses = await service.GetStatusAsync();
        return statuses.Count == 0
            ? "language server service reported no statuses"
            : string.Join("; ", statuses.Select(status =>
                $"{status.ServerId}@{status.Root}: {status.Status} {status.Message}"));
    }

    private static string FormatMiddlewareSmokeOutput(AfterFunctionContext context)
    {
        var state = context.GetMiddlewareState<LanguageServerState>();
        var lines = new List<string>
        {
            "<middleware_smoke_result>",
            "  <returned_tool_result>",
            context.Result?.ToString() ?? string.Empty,
            "  </returned_tool_result>",
            "  <documents>"
        };

        foreach (var document in state?.DocumentsByPath.Values ?? [])
        {
            lines.Add(
                $"    <document path=\"{EscapeXml(document.Path)}\" language=\"{EscapeXml(document.LanguageId)}\" version=\"{document.Version}\" opened=\"{document.Opened}\" dirty=\"{document.DirtySinceLastDiagnostics}\" />");
        }

        lines.Add("  </documents>");
        lines.Add("  <diagnostic_sets>");

        foreach (var diagnosticSet in state?.DiagnosticsByPath.Values ?? [])
        {
            lines.Add(
                $"    <diagnostic_set path=\"{EscapeXml(diagnosticSet.Path)}\" server=\"{EscapeXml(diagnosticSet.ServerId)}\" source=\"{diagnosticSet.Source}\" count=\"{diagnosticSet.Diagnostics.Count}\" partial=\"{diagnosticSet.Partial}\" />");
        }

        lines.Add("  </diagnostic_sets>");
        lines.Add("</middleware_smoke_result>");
        return string.Join(System.Environment.NewLine, lines);
    }

    private static string EscapeXml(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private sealed record TypeScriptSmokeDependencyStatus(bool IsReady, string Message);

    private sealed class CapturingEventCoordinator : IEventCoordinator
    {
        private readonly EventCoordinator _inner = new();

        public List<Event> Captured { get; } = [];

        public IEventFlowRegistry EventFlows => _inner.EventFlows;

        public void Emit(Event evt)
            => Captured.Add(evt);

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
        {
            Emit(evt);
            return ValueTask.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(
            Func<TEvent, ValueTask> handler,
            EventSubscriptionOptions? options = null)
            where TEvent : Event
            => _inner.Subscribe(handler, options);

        public IDisposable SubscribeAny(
            Func<Event, ValueTask> handler,
            EventSubscriptionOptions? options = null)
            => _inner.SubscribeAny(handler, options);

        public EventInbox<TEvent> CreateInbox<TEvent>(
            EventInboxOptions? options = null)
            where TEvent : Event
            => _inner.CreateInbox<TEvent>(options);

        public EventInbox<Event> CreateChannelInbox(
            EventChannel channel,
            EventInboxOptions? options = null)
            => _inner.CreateChannelInbox(channel, options);

        public void SetParent(IEventCoordinator parent)
            => _inner.SetParent(parent);

        public RequestHandle StartRequest<TRequest, TResponse>(
            TRequest request,
            RequestOptions? options = null)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent
            => _inner.StartRequest<TRequest, TResponse>(request, options);

        public Task<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            TimeSpan timeout,
            CancellationToken ct = default)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent
            => _inner.RequestAsync<TRequest, TResponse>(request, timeout, ct);

        public RespondResult Respond(Event response)
            => _inner.Respond(response);

        public RespondResult Respond(string requestId, Event response)
            => _inner.Respond(requestId, response);

        public EventCoordinatorStats GetStats()
            => _inner.GetStats();
    }

    private sealed class FakeLanguageServerService : ILanguageServerService
    {
        public bool HasServer { get; init; }
        public bool Opened { get; init; }
        public IReadOnlyList<LanguageServerDiagnosticSet> Diagnostics { get; init; } = [];
        public IReadOnlyList<LanguageServerStatus> Statuses { get; init; } = [];
        public List<LanguageServerDocumentOpenRequest> OpenRequests { get; } = [];
        public List<LanguageServerDocumentChangeRequest> ChangeRequests { get; } = [];
        public List<LanguageServerDocumentSaveRequest> SaveRequests { get; } = [];
        public List<LanguageServerDocumentCloseRequest> CloseRequests { get; } = [];
        public List<LanguageServerWatchedFileChangeRequest> WatchedFileChangeRequests { get; } = [];
        public List<LanguageServerDiagnosticRequest> DiagnosticRequests { get; } = [];

        public ValueTask<IReadOnlyList<LanguageServerStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Statuses);

        public ValueTask<bool> HasServerForFileAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(HasServer);

        public ValueTask<LanguageServerDocumentResolution> ResolveDocumentAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var normalizedPath = Path.GetFullPath(path, Directory.GetCurrentDirectory());
            return ValueTask.FromResult(new LanguageServerDocumentResolution
            {
                Path = normalizedPath,
                Uri = new Uri(normalizedPath).AbsoluteUri,
                Servers = HasServer
                    ?
                    [
                        new LanguageServerResolvedServer
                        {
                            ServerId = "csharp",
                            Root = Path.GetDirectoryName(normalizedPath) ?? Directory.GetCurrentDirectory(),
                            LanguageId = "csharp"
                        }
                    ]
                    : []
            });
        }

        public ValueTask<LanguageServerOpenResult> OpenDocumentAsync(
            LanguageServerDocumentOpenRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(request);
            return ValueTask.FromResult(new LanguageServerOpenResult
            {
                Path = request.Path,
                Uri = request.Uri,
                LanguageId = request.LanguageId,
                Version = request.Version,
                PositionEncoding = request.PositionEncoding,
                Opened = Opened
            });
        }

        public ValueTask<LanguageServerChangeResult> ChangeDocumentAsync(
            LanguageServerDocumentChangeRequest request,
            CancellationToken cancellationToken = default)
        {
            ChangeRequests.Add(request);
            return ValueTask.FromResult(new LanguageServerChangeResult
            {
                Path = request.Path,
                Version = request.Version
            });
        }

        public ValueTask SaveDocumentAsync(
            LanguageServerDocumentSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(request);
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseDocumentAsync(
            LanguageServerDocumentCloseRequest request,
            CancellationToken cancellationToken = default)
        {
            CloseRequests.Add(request);
            return ValueTask.CompletedTask;
        }

        public ValueTask NotifyWatchedFileChangedAsync(
            LanguageServerWatchedFileChangeRequest request,
            CancellationToken cancellationToken = default)
        {
            WatchedFileChangeRequests.Add(request);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<LanguageServerDiagnosticSet>> GetDiagnosticsAsync(
            LanguageServerDiagnosticRequest request,
            CancellationToken cancellationToken = default)
        {
            DiagnosticRequests.Add(request);
            return ValueTask.FromResult(Diagnostics);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingNullProvider : ILanguageServerProvider
    {
        public int ResolveCount { get; private set; }

        public ValueTask<string?> ResolveRootAsync(
            LanguageServerRootContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(context.WorkspaceRoot);

        public ValueTask<LanguageServerLaunchDescriptor?> ResolveLaunchAsync(
            LanguageServerLaunchContext context,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return ValueTask.FromResult<LanguageServerLaunchDescriptor?>(null);
        }

        public ValueTask<LanguageServerInitialization> CreateInitializationAsync(
            LanguageServerInitializationContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new LanguageServerInitialization());
    }
}
