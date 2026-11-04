namespace HPD.Environment.HostProcess.Tests.Workspaces;

using FluentAssertions;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class HostProcessWorkspaceFileStoreTests
    : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "hpd-workspace-tests",
        Guid.NewGuid().ToString("N"));
    private readonly HostProcessProviderModule _module = new();

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _module.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Read_write_search_and_conditional_mutation_are_root_scoped()
    {
        await using WorkspaceFileStoreAttachment attachment =
            await OpenAsync(AccessMode.ReadWrite);
        var readme = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            "src/readme.txt");

        WorkspaceMutationResult created = await attachment.Files.WriteTextAsync(
            readme,
            "first\nneedle here\n",
            WorkspaceWriteCondition.RequireCreate);
        created.Outcome.Should().Be(
            WorkspaceMutationOutcome.Succeeded);
        created.Version.Should().NotBeNull();

        WorkspaceTextSnapshot read =
            (await attachment.Files.ReadTextAsync(
                readme,
                new WorkspaceTextReadOptions
                {
                    StartLine = 2,
                    MaxLines = 1,
                }))!;
        read.Content.Should().Be("needle here\n");
        read.StartLine.Should().Be(2);
        read.EndLine.Should().Be(2);

        WorkspaceBinarySnapshot binary =
            (await attachment.Files.ReadBytesAsync(
                readme,
                new WorkspaceByteReadOptions
                {
                    Offset = 6,
                    MaxBytes = 6,
                }))!;
        System.Text.Encoding.UTF8.GetString(
                binary.Content.Span)
            .Should().Be("needle");

        WorkspaceSearchResultSet search =
            await attachment.Files.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = new WorkspacePath(
                        new WorkspaceRootId("workspace"),
                        "src"),
                    Query = "needle",
                    GlobPattern = "*.txt",
                });
        search.Results.Should().ContainSingle();
        search.Results[0].Path.Should().Be(readme);
        search.Results[0].Line.Should().Be(2);

        WorkspaceMutationResult stale =
            await attachment.Files.WriteTextAsync(
                readme,
                "stale",
                new WorkspaceWriteCondition
                {
                    ExpectedVersion = new WorkspaceContentVersion(
                        null,
                        new Digest("sha256", "not-current"),
                        null,
                        default),
                });
        stale.Outcome.Should().Be(WorkspaceMutationOutcome.Stale);

        WorkspaceMutationResult updated =
            await attachment.Files.WriteTextAsync(
                readme,
                "updated\n",
                new WorkspaceWriteCondition
                {
                    ExpectedVersion = created.Version,
                });
        updated.Outcome.Should().Be(
            WorkspaceMutationOutcome.Succeeded);
        (await attachment.Files.ReadTextAsync(
            readme,
            new WorkspaceTextReadOptions()))!
            .Content.Should().Be("updated\n");
    }

    [Fact]
    public async Task Listing_and_mapping_return_only_logical_paths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "nested", "a.cs"),
            "class A {}");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "nested", "b.txt"),
            "text");
        await using WorkspaceFileStoreAttachment attachment =
            await OpenAsync(AccessMode.ReadOnly);

        WorkspaceListResultSet listing =
            await attachment.Files.ListAsync(
                new WorkspacePath(
                    new WorkspaceRootId("workspace"),
                    string.Empty),
                new WorkspaceListOptions
                {
                    Recursive = true,
                    GlobPattern = "**/*.cs",
                });
        listing.Entries.Should().ContainSingle();
        listing.Entries[0].Path.Should().Be(new WorkspacePath(
            new WorkspaceRootId("workspace"),
            "nested/a.cs"));

        WorkspaceExecutionPath execution =
            attachment.Paths.ToExecutionPath(listing.Entries[0].Path);
        execution.Value.Should().Be(
            Path.Combine(_root, "nested", "a.cs"));
        attachment.Paths.TryFromExecutionPath(execution)
            .Should().Be(listing.Entries[0].Path);
        attachment.Paths.TryFromExecutionFileUri(
                attachment.Paths.ToExecutionFileUri(
                    listing.Entries[0].Path))
            .Should().Be(listing.Entries[0].Path);
    }

    [Fact]
    public async Task ReadOnly_traversal_and_symlink_requests_fail_closed()
    {
        await using WorkspaceFileStoreAttachment attachment =
            await OpenAsync(AccessMode.ReadOnly);
        var path = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            "file.txt");

        WorkspaceMutationResult mutation =
            await attachment.Files.WriteTextAsync(
                path,
                "denied",
                WorkspaceWriteCondition.RequireCreate);
        mutation.Outcome.Should().Be(
            WorkspaceMutationOutcome.AccessDenied);

        Func<Task> traversal = async () =>
            await attachment.Files.GetInfoAsync(
                new WorkspacePath(
                    new WorkspaceRootId("workspace"),
                    "../outside.txt"));
        await traversal.Should().ThrowAsync<UnauthorizedAccessException>();

        string symlink = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(symlink, Path.GetTempPath());
        try
        {
            Func<Task> symlinkRead = async () =>
                await attachment.Files.GetInfoAsync(
                    new WorkspacePath(
                        new WorkspaceRootId("workspace"),
                        "link"));
            await symlinkRead.Should()
                .ThrowAsync<UnauthorizedAccessException>();
        }
        finally
        {
            Directory.Delete(symlink);
        }
    }

    [Fact]
    public async Task Projection_runtime_verifies_and_releases_a_unit_scoped_projection()
    {
        File.WriteAllText(Path.Combine(_root, "file.txt"), "content");
        await using var module = new HostProcessProviderModule();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(module);
        var runtime = new InMemoryEnvironmentRuntime(registry);

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            host = await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    HostProcessProviderDescriptor.ProviderId,
                Platform =
                    HostProcessProviderDescriptor.CurrentPlatform(),
            });
        var hostRef = new ResourceRef<RuntimeHost>(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = hostRef,
                });
        var unitRef = new ResourceRef<ExecutionUnit>(
            unit.Metadata.Id,
            unit.Metadata.Scope,
            unit.Metadata.Generation);

        ResourceSnapshot<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> projection =
            await runtime.EnsureAsync(new ContentProjectionSpec
            {
                Source = new ContentSelector
                {
                    Kind = ContentSelectorKind.HostPath,
                    HostPath = new HostPathSelection(
                        new HostPath(_root),
                        HostPathKind.Directory),
                },
                Target = new ContentProjectionTarget
                {
                    Host = hostRef,
                    Unit = unitRef,
                },
                View = new ProjectionView
                {
                    Kind = ProjectionViewKind.FilesystemTree,
                    GuestPath = new GuestPath(_root),
                },
                Role = ContentProjectionRole.Workspace,
                AccessMode = AccessMode.ReadOnly,
                SecurityPolicy =
                    ContentProjectionSecurityPolicy.Default with
                    {
                        AllowHostPathSource = true,
                        ReadOnlyEnforcement =
                            ReadOnlyEnforcementPolicy.BestEffort,
                    },
            });
        projection.Status.ProjectionPhase.Should().Be(
            ContentProjectionPhase.Projected);

        var entries = new List<ContentProjectionEntry>();
        await runtime.EnumerateAsync(
            new ResourceRef<ContentProjection>(
                projection.Metadata.Id,
                projection.Metadata.Scope,
                projection.Metadata.Generation),
            new CollectingProjectionSink(entries));
        entries.Should().Contain(entry =>
            entry.Path.Value == Path.Combine(_root, "file.txt"));

        await runtime.ReleaseAsync(new ResourceRef<ContentProjection>(
            projection.Metadata.Id,
            projection.Metadata.Scope,
            projection.Metadata.Generation));
        await runtime.DeleteExecutionUnitAsync(unitRef);
        RuntimeHostDeletionResult deleted =
            await runtime.DeleteHostAsync();
        deleted.Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task Search_preserves_output_modes_context_multiline_and_filters()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "ignored"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, ".gitignore"),
            "ignored/\n*.log\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "a.cs"),
            "before\nneedle alpha\nother\nafter\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "ignored", "b.cs"),
            "needle alpha\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "debug.log"),
            "needle alpha\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, ".hidden.cs"),
            "needle alpha\n");
        await using WorkspaceFileStoreAttachment attachment =
            await OpenAsync(AccessMode.ReadOnly);
        var root = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            string.Empty);

        WorkspaceSearchResultSet files =
            await attachment.Files.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = "needle",
                    OutputMode =
                        WorkspaceSearchOutputMode.FilesWithMatches,
                });
        files.Files.Should().Equal(
            new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "src/a.cs"));

        WorkspaceSearchResultSet counts =
            await attachment.Files.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = "needle",
                    OutputMode = WorkspaceSearchOutputMode.Count,
                    IncludeGlobs = ["**/*.cs"],
                    ExcludeGlobs = ["ignored/**"],
                });
        counts.Counts.Should().ContainSingle();
        counts.Counts[0].Count.Should().Be(1);

        WorkspaceSearchResultSet context =
            await attachment.Files.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = "needle",
                    BeforeContext = 1,
                    AfterContext = 1,
                });
        context.Results.Should().ContainSingle();
        context.Results[0].ContextLines.Select(line => line.Text)
            .Should().Equal("before", "other");

        WorkspaceSearchResultSet multiline =
            await attachment.Files.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = @"alpha\s+other",
                    Multiline = true,
                    ExcludeGlobs = ["ignored/**", "*.log", ".hidden.cs"],
                });
        multiline.Results.Should().ContainSingle();
        multiline.Results[0].Line.Should().Be(2);
        multiline.Results[0].LineText.Should().Contain("other");
    }

    [Fact]
    public async Task Listing_applies_hidden_ignore_depth_glob_and_result_limits()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "ignored"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, ".gitignore"),
            "ignored/\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "one.cs"),
            "one");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "two.cs"),
            "two");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "nested", "two.cs"),
            "two");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "ignored", "three.cs"),
            "three");
        await File.WriteAllTextAsync(
            Path.Combine(_root, ".hidden.cs"),
            "hidden");
        await using WorkspaceFileStoreAttachment attachment =
            await OpenAsync(AccessMode.ReadOnly);

        WorkspaceListResultSet listing =
            await attachment.Files.ListAsync(
                new WorkspacePath(
                    new WorkspaceRootId("workspace"),
                    string.Empty),
                new WorkspaceListOptions
                {
                    Recursive = true,
                    GlobPattern = "**/*.cs",
                    MaxDepth = 2,
                    MaxEntries = 1,
                });
        listing.Status.Should().Be(WorkspaceListStatus.Truncated);
        listing.Entries.Should().ContainSingle();
        listing.IgnoredCount.Should().BeGreaterThan(0);
    }

    private async ValueTask<WorkspaceFileStoreAttachment> OpenAsync(
        AccessMode access)
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(_module);
        IWorkspaceFileStoreProvider provider =
            registry.WorkspaceFileStoreProviders.Single();
        var unit = new TargetHandle<ExecutionUnit>(
            new TargetRoute
            {
                Kind = new TargetKind("execution-unit"),
                Scope = new ResourceScope("test"),
                ProviderId =
                    HostProcessProviderDescriptor.ProviderId,
            },
            TargetHandleLifetime.Lease,
            TargetHandleAuthority.Observe |
            TargetHandleAuthority.Read |
            TargetHandleAuthority.Write);
        return await provider.OpenAsync(
            new WorkspaceFileStoreSpec
            {
                RootId = new WorkspaceRootId("workspace"),
                Source = new ContentSelector
                {
                    Kind = ContentSelectorKind.HostPath,
                    HostPath = new HostPathSelection(
                        new HostPath(_root),
                        HostPathKind.Directory),
                },
                ExecutionPath =
                    new WorkspaceExecutionPath(_root),
                Access = access,
                PathSemantics = new WorkspacePathSemantics(
                    OperatingSystem.IsLinux(),
                    SymlinkEvaluationPolicy.DenySymlinks),
            },
            unit);
    }

    private sealed class CollectingProjectionSink(
        ICollection<ContentProjectionEntry> entries)
        : IContentProjectionEntrySink
    {
        public ValueTask OnEntryAsync(
            ContentProjectionEntry entry,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }
}
