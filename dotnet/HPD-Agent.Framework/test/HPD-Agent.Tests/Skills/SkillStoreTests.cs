using System.Text;
using HPD.Agent.Tests.TestToolHarnesses;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Skills;

public sealed class SkillStoreTests
{
    [Fact]
    public async Task PackageFactory_PersistsGeneratedCanonicalContract()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        var package = CreatePackage("generated", "1", "Generated contract instructions.") with
        {
            Scripts =
            [
                SkillPackageScript.Create(
                    "summarize",
                    "Summarizes a file.",
                    "python",
                    new MemoryStream(Encoding.UTF8.GetBytes("print('ok')")),
                    GeneratedScriptInput.AIContract)
            ]
        };

        var installed = await store.InstallAsync(package);
        var script = Assert.Single(installed.Scripts);

        Assert.Equal(
            GeneratedScriptInput.AIContract.CanonicalSchemaFingerprint,
            script.SchemaFingerprint);
        Assert.Equal(
            GeneratedScriptInput.AIContract.JsonSchema.GetRawText(),
            script.ParametersSchema.GetRawText());
    }

    [Fact]
    public async Task ContentBackedStore_ReconstructsPublishedInventory()
    {
        var content = new InMemoryContentStore();
        var first = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        await first.InstallAsync(CreatePackage("analysis", "1", "Persisted instructions."));

        var reconstructed = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        var stored = Assert.Single(await reconstructed.ListAsync(new SkillQuery()));

        Assert.Equal("analysis", stored.Manifest.Id);
        await using var instructions = await content.OpenReadAsync(stored.Instructions);
        Assert.NotNull(instructions);
    }

    [Fact]
    public async Task LocalContentBackedStore_UpdateAndDeleteSurviveReconstruction()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-skill-store-{Guid.NewGuid():N}");
        try
        {
            var content = new LocalFileContentStore(path);
            var first = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
            await first.InstallAsync(CreatePackage("analysis", "1", "Version one."));
            await first.UpdateAsync("analysis", CreatePackage("analysis", "2", "Version two."), "1");

            var afterUpdate = new ContentStoreSkillStore(
                new LocalFileContentStore(path), ContentStoreScopes.Skills);
            var current = Assert.Single(await afterUpdate.ListAsync(new SkillQuery()));
            Assert.Equal("2", current.Manifest.Version);

            await afterUpdate.DeleteAsync("analysis", "2");
            var afterDelete = new ContentStoreSkillStore(
                new LocalFileContentStore(path), ContentStoreScopes.Skills);
            Assert.Empty(await afterDelete.ListAsync(new SkillQuery()));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingStoreInstance_RefreshesPublicationsWrittenByAnotherInstance()
    {
        var content = new InMemoryContentStore();
        var writer = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        var reader = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        await writer.InstallAsync(CreatePackage("analysis", "1", "Version one."));
        Assert.Equal("1", Assert.Single(await reader.ListAsync(new SkillQuery())).Manifest.Version);

        await writer.UpdateAsync("analysis", CreatePackage("analysis", "2", "Version two."), "1");

        Assert.Equal("2", Assert.Single(await reader.ListAsync(new SkillQuery())).Manifest.Version);
    }

    [Fact]
    public async Task Reconstruction_IgnoresMalformedPublicationAndReportsBoundedDiagnostic()
    {
        var content = new InMemoryContentStore();
        await content.WriteTextAsync(
            ContentStoreScopes.Skills,
            "{not-json",
            new ContentMetadata
            {
                Name = "skill-current:broken",
                ContentType = "application/json",
                Tags = new Dictionary<string, string> { ["kind"] = "skill-publication" }
            });
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        SkillStoreDiagnostic? diagnostic = null;
        store.Diagnostic += value => diagnostic = value;

        Assert.Empty(await store.ListAsync(new SkillQuery()));
        Assert.NotNull(diagnostic);
        Assert.Equal("InvalidPublication", diagnostic!.Category);
        Assert.DoesNotContain("{not-json", diagnostic.Message);
    }

    [Fact]
    public async Task WatchAsync_BroadcastsChangesToEverySubscriber()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = NextChangeAsync(store, timeout.Token);
        var second = NextChangeAsync(store, timeout.Token);
        await Task.Yield();

        await store.InstallAsync(CreatePackage("analysis", "1", "Instructions."), timeout.Token);

        Assert.Equal("analysis", (await first).SkillId);
        Assert.Equal("analysis", (await second).SkillId);
    }

    [Fact]
    public async Task AddSkillsFromStore_UsesExplicitContentBackedStore()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        await store.InstallAsync(CreatePackage("runtime", "1", "Runtime instructions."));

        var agent = await new AgentBuilder(new AgentConfig { Name = "stored-skill-test" })
            .WithSkillStore(store)
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillsFromStore())
            .BuildAsync();
        try
        {
            Assert.Contains(agent.DefaultOptions!.Tools!, tool =>
                tool is Microsoft.Extensions.AI.AIFunction function && function.Name == "runtime_skill");
        }
        finally { await agent.DisposeAsync(); }
    }

    [Fact]
    public async Task AddSkillsFromStore_WithoutConfiguredStore_FailsClearly()
    {
        var builder = new AgentBuilder(new AgentConfig { Name = "missing-skill-store" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillsFromStore());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync());
        Assert.Contains("no content-backed ISkillStore", error.Message);
    }

    [Fact]
    public void AddHPDSkills_PreservesCustomContentStore()
    {
        var content = new InMemoryContentStore();
        var services = new ServiceCollection();
        services.AddSingleton<IContentStore>(content);
        services.AddHPDSkills();
        using var provider = services.BuildServiceProvider();

        var store = Assert.IsType<ContentStoreSkillStore>(provider.GetRequiredService<ISkillStore>());
        Assert.Same(content, store.ContentStore);
    }

    [Fact]
    public async Task AddSkillsFromStore_ResolvesDIStore()
    {
        var services = new ServiceCollection();
        services.AddHPDSkills();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ISkillStore>()
            .InstallAsync(CreatePackage("di", "1", "DI instructions."));

        var agent = await new AgentBuilder(new AgentConfig { Name = "di-skill-store" })
            .WithServiceProvider(provider)
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillsFromStore())
            .BuildAsync();
        try
        {
            Assert.Contains(agent.DefaultOptions!.Tools!, tool =>
                tool is Microsoft.Extensions.AI.AIFunction function && function.Name == "di_skill");
        }
        finally { await agent.DisposeAsync(); }
    }

    [Fact]
    public async Task WithSkillStore_TakesPrecedenceOverDIStore()
    {
        var services = new ServiceCollection();
        services.AddHPDSkills();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ISkillStore>()
            .InstallAsync(CreatePackage("di", "1", "DI instructions."));
        var explicitContent = new InMemoryContentStore();
        var explicitStore = new ContentStoreSkillStore(explicitContent, ContentStoreScopes.Skills);
        await explicitStore.InstallAsync(CreatePackage("explicit", "1", "Explicit instructions."));

        var agent = await new AgentBuilder(new AgentConfig { Name = "explicit-skill-store" })
            .WithServiceProvider(provider)
            .WithSkillStore(explicitStore)
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillsFromStore())
            .BuildAsync();
        try
        {
            var names = agent.DefaultOptions!.Tools!.OfType<Microsoft.Extensions.AI.AIFunction>()
                .Select(function => function.Name).ToArray();
            Assert.Contains("explicit_skill", names);
            Assert.DoesNotContain("di_skill", names);
        }
        finally { await agent.DisposeAsync(); }
    }

    [Fact]
    public async Task SharedStore_CanSelectDifferentSkillsForDifferentHarnesses()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        var data = CreatePackage("data", "1", "Data instructions.");
        data = data with
        {
            Manifest = data.Manifest with
            {
                Tags = new Dictionary<string, string> { ["domain"] = "data" }
            }
        };
        var support = CreatePackage("support", "1", "Support instructions.");
        support = support with
        {
            Manifest = support.Manifest with
            {
                Tags = new Dictionary<string, string> { ["domain"] = "support" }
            }
        };
        await store.InstallAsync(data);
        await store.InstallAsync(support);

        var agent = await new AgentBuilder(new AgentConfig { Name = "selected-skill-store" })
            .WithSkillStore(store)
            .WithToolHarness<CombinedCapabilitiesTools>(options =>
                options.AddSkillsFromStore(SkillQuery.WithTag("domain", "data")))
            .WithToolHarness<NamedWeatherToolHarness>(options =>
                options.AddSkillsFromStore(SkillQuery.WithTag("domain", "support")))
            .BuildAsync();
        try
        {
            var names = agent.DefaultOptions!.Tools!.OfType<Microsoft.Extensions.AI.AIFunction>()
                .Select(function => function.Name).ToArray();
            Assert.Contains("data_skill", names);
            Assert.Contains("support_skill", names);
        }
        finally { await agent.DisposeAsync(); }
    }

    [Fact]
    public async Task SharedStore_UnfilteredAcrossHarnesses_FailsWithSelectorGuidance()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        var builder = new AgentBuilder(new AgentConfig { Name = "ambiguous-skill-store" })
            .WithSkillStore(store)
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillsFromStore())
            .WithToolHarness<NamedWeatherToolHarness>(options => options.AddSkillsFromStore());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync());
        Assert.Contains("SkillQuery.WithTag", error.Message);
    }
    [Fact]
    public async Task Install_SourceAndDelete_KeepImmutablePublishedBytesReadable()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentScope.Create("installed-skills"));
        var installed = await store.InstallAsync(CreatePackage("analysis", "1", "Version one."));
        var source = new ContentStoreSkillSource(store, content);

        var skill = Assert.Single(await source.GetSkillsAsync(
            new SkillSourceContext("agent", "DataTools", null, null),
            default));
        Assert.Equal("Version one.", await skill.Instructions(null!, default));

        await store.DeleteAsync("analysis", "1");

        Assert.Null(await store.GetAsync("analysis"));
        await using var retained = await content.OpenReadAsync(installed.Instructions);
        Assert.NotNull(retained);
    }

    [Fact]
    public async Task Update_RequiresExpectedVersionAndPublishesNewAddresses()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentScope.Create("installed-skills"));
        var first = await store.InstallAsync(CreatePackage("analysis", "1", "Version one."));

        await Assert.ThrowsAsync<ContentConflictException>(async () =>
            await store.UpdateAsync("analysis", CreatePackage("analysis", "2", "Version two."), "wrong"));
        var second = await store.UpdateAsync(
            "analysis",
            CreatePackage("analysis", "2", "Version two."),
            "1");

        Assert.NotEqual(first.Instructions.ContentId, second.Instructions.ContentId);
        Assert.Equal("2", (await store.GetAsync("analysis"))!.Manifest.Version);
    }

    [Fact]
    public async Task Update_RequiresANewImmutablePackageVersion()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        await store.InstallAsync(CreatePackage("analysis", "1", "Version one."));

        await Assert.ThrowsAsync<ContentConflictException>(async () =>
            await store.UpdateAsync("analysis", CreatePackage("analysis", "1", "Different bytes."), "1"));
    }

    [Fact]
    public async Task Install_RejectsEmptyInstructionsAndDuplicateCapabilityNames()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentStoreScopes.Skills);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.InstallAsync(CreatePackage("empty", "1", "")));

        var duplicate = new SkillPackage
        {
            Manifest = Manifest("duplicate", "1"),
            Instructions = Stream("Instructions."),
            Resources =
            [
                new SkillPackageResource
                {
                    Name = "same",
                    Description = "Reads the same capability.",
                    Content = Stream("resource")
                }
            ],
            Scripts =
            [
                new SkillPackageScript
                {
                    Name = "same",
                    Description = "Runs the same capability.",
                    Content = Stream("script"),
                    Runtime = "demo",
                    ParametersSchema = EmptySchema()
                }
            ]
        };
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.InstallAsync(duplicate));
    }

    [Fact]
    public async Task InstalledResourcesAndScripts_NormalizeToStructuredCapabilities()
    {
        var content = new InMemoryContentStore();
        var store = new ContentStoreSkillStore(content, ContentScope.Create("installed-skills"));
        await store.InstallAsync(new SkillPackage
        {
            Manifest = Manifest("analysis", "1"),
            Instructions = Stream("Analyze safely."),
            Resources =
            [
                new SkillPackageResource
                {
                    Name = "read_guide",
                    Description = "Reads the installed analysis guide.",
                    Content = Stream("Guide."),
                    ContentType = "text/markdown"
                }
            ],
            Scripts =
            [
                new SkillPackageScript
                {
                    Name = "normalize",
                    Description = "Normalizes the active dataset and returns an artifact.",
                    Content = Stream("print('normalize')"),
                    Runtime = "python",
                    ParametersSchema = EmptySchema()
                }
            ]
        });

        var source = new ContentStoreSkillSource(store, content);
        var skill = Assert.Single(await source.GetSkillsAsync(
            new SkillSourceContext("agent", "DataTools", null, null), default));

        Assert.Contains(skill.Capabilities, capability => capability is ContentStoreSkillResource);
        Assert.Contains(skill.Capabilities, capability => capability is SkillScript);
    }

    private static SkillPackage CreatePackage(string id, string version, string instructions)
        => new() { Manifest = Manifest(id, version), Instructions = Stream(instructions) };

    private static SkillPackageManifest Manifest(string id, string version)
        => new()
        {
            Id = id,
            Name = id + "_skill",
            Description = "Provides installed analysis guidance.",
            Version = version
        };

    private static MemoryStream Stream(string value)
        => new(Encoding.UTF8.GetBytes(value));

    private static System.Text.Json.JsonElement EmptySchema()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""");
        return document.RootElement.Clone();
    }

    private static async Task<SkillStoreChange> NextChangeAsync(
        IWatchableSkillStore store,
        CancellationToken cancellationToken)
    {
        await foreach (var change in store.WatchAsync(new SkillQuery(), cancellationToken))
            return change;
        throw new InvalidOperationException("The change feed completed unexpectedly.");
    }
}
