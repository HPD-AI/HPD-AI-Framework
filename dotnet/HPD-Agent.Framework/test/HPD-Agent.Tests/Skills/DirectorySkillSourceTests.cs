namespace HPD.Agent.Tests.Skills;

public sealed class DirectorySkillSourceTests
{
    [Fact]
    public async Task StrictImport_ReadsDescriptionsAndPinsResourceSnapshot()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "references"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "SKILL.md"), """
            ---
            id: analysis
            name: data_analysis
            description: Provides data analysis guidance.
            version: 1
            ---
            Validate the dataset before analysis.
            """);
        var resourcePath = Path.Combine(directory.Path, "references", "guide.md");
        await File.WriteAllTextAsync(resourcePath, "Version one.");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "skill.json"), """
            { "resources": { "references/guide.md": "Reads the validation guide." } }
            """);
        var source = new DirectorySkillSource(directory.Path);

        var first = Assert.Single(await source.GetSkillsAsync(Context(), default));
        var firstResource = Assert.IsType<ContentStoreSkillResource>(Assert.Single(first.Capabilities));
        await File.WriteAllTextAsync(resourcePath, "Version two.");
        var second = Assert.Single(await source.GetSkillsAsync(Context(), default));
        var secondResource = Assert.IsType<ContentStoreSkillResource>(Assert.Single(second.Capabilities));

        Assert.Equal("Version one.", await firstResource.ReadAsync(null!, default));
        Assert.Equal("Version two.", await secondResource.ReadAsync(null!, default));
        Assert.Equal("Validate the dataset before analysis.", await first.Instructions(null!, default));
    }

    [Fact]
    public async Task StrictImport_RejectsUndescribedResource()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "references"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "SKILL.md"), """
            ---
            name: data_analysis
            description: Provides data analysis guidance.
            ---
            Validate the dataset.
            """);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "references", "guide.md"), "Guide.");
        var source = new DirectorySkillSource(directory.Path, SkillDirectoryImportMode.Strict);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await source.GetSkillsAsync(Context(), default));

        Assert.Contains("requires a description", exception.Message);
    }

    [Fact]
    public async Task CompatibilityImport_DerivesDescriptionsAndRuntimeWithoutExecutingScript()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "SKILL.md"), """
            ---
            name: data_analysis
            description: Provides data analysis guidance.
            ---
            Normalize before analysis.
            """);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scripts", "normalize.py"), "raise Exception('must not run')");
        var source = new DirectorySkillSource(directory.Path, SkillDirectoryImportMode.Compatibility);

        var skill = Assert.Single(await source.GetSkillsAsync(Context(), default));
        var script = Assert.IsType<SkillScript>(Assert.Single(skill.Capabilities));

        Assert.Equal("python", script.Reference.Runtime);
        Assert.True(script.RequiresPermission);
    }

    [Fact]
    public async Task Import_AcceptsUtf8BomAndDiscoversMultipleSkillsDeterministically()
    {
        using var directory = new TemporaryDirectory();
        await WriteSkillAsync(Path.Combine(directory.Path, "z-skill"), "z_skill", bom: true);
        await WriteSkillAsync(Path.Combine(directory.Path, "a-skill"), "a_skill");
        var source = new DirectorySkillSource(directory.Path);

        var skills = await source.GetSkillsAsync(Context(), default);

        Assert.Equal(["a_skill", "z_skill"], skills.Select(skill => skill.Name));
    }

    [Theory]
    [InlineData("|", "First line.\nSecond line.")]
    [InlineData(">", "First line. Second line.")]
    public async Task Import_ParsesYamlBlockScalarDescriptions(string indicator, string expected)
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "SKILL.md"), $$"""
            ---
            name: block_scalar
            description: {{indicator}}
              First line.
              Second line.
            ---
            Follow the instructions.
            """);
        var source = new DirectorySkillSource(directory.Path);

        var skill = Assert.Single(await source.GetSkillsAsync(Context(), default));

        Assert.Equal(expected, skill.Description);
    }

    [Theory]
    [InlineData("description: Valid.", "")]
    [InlineData("", "Instructions.")]
    public async Task Import_RejectsMissingRequiredFrontmatterOrInstructions(
        string frontmatter,
        string instructions)
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "SKILL.md"), $$"""
            ---
            name: test_skill
            {{frontmatter}}
            ---
            {{instructions}}
            """);
        var source = new DirectorySkillSource(directory.Path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await source.GetSkillsAsync(Context(), default));
    }

    [Fact]
    public async Task Import_DoesNotTreatNestedSkillDocumentAsIndependentSkill()
    {
        using var directory = new TemporaryDirectory();
        await WriteSkillAsync(directory.Path, "parent_skill");
        await WriteSkillAsync(Path.Combine(directory.Path, "references", "nested"), "nested_skill");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "skill.json"), """
            { "resources": { "references/nested/SKILL.md": "Reads nested documentation." } }
            """);
        var source = new DirectorySkillSource(directory.Path);

        var skill = Assert.Single(await source.GetSkillsAsync(Context(), default));

        Assert.Equal("parent_skill", skill.Name);
        Assert.Single(skill.Capabilities);
    }

    [Fact]
    public async Task Import_RejectsSymlinkedResourceDirectoryBeforeReadingOutsideFiles()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        await WriteSkillAsync(directory.Path, "safe_skill");
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "secret.md"), "outside secret");
        Directory.CreateSymbolicLink(Path.Combine(directory.Path, "references"), outside.Path);
        var source = new DirectorySkillSource(directory.Path, SkillDirectoryImportMode.Compatibility);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await source.GetSkillsAsync(Context(), default));

        Assert.Contains("Symbolic links are not allowed", exception.Message);
    }

    [Fact]
    public async Task WatchAsync_ReportsNestedResourceChangesForFullReconciliation()
    {
        using var directory = new TemporaryDirectory();
        await WriteSkillAsync(directory.Path, "watched_skill");
        var references = Path.Combine(directory.Path, "references");
        Directory.CreateDirectory(references);
        var resource = Path.Combine(references, "guide.md");
        await File.WriteAllTextAsync(resource, "Version one.");
        var source = new DirectorySkillSource(directory.Path, SkillDirectoryImportMode.Compatibility);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var watcher = source.WatchAsync(Context(), timeout.Token).GetAsyncEnumerator();
        var next = watcher.MoveNextAsync().AsTask();
        await Task.Delay(50, timeout.Token);

        await File.WriteAllTextAsync(resource, "Version two.", timeout.Token);

        Assert.True(await next);
        Assert.Equal(SkillSourceChangeKind.Reset, watcher.Current.Kind);
    }

    private static async Task WriteSkillAsync(string path, string name, bool bom = false)
    {
        Directory.CreateDirectory(path);
        var content = $$"""
            ---
            name: {{name}}
            description: Test skill {{name}}.
            ---
            Follow the test instructions.
            """;
        if (bom)
            content = "\uFEFF" + content;
        await File.WriteAllTextAsync(Path.Combine(path, "SKILL.md"), content);
    }

    private static SkillSourceContext Context() => new("agent", "DataTools", null, null);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hpd-skill-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
