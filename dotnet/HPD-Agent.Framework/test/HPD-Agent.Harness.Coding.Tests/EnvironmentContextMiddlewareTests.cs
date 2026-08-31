using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentDirectoryCollection
{
    public const string Name = "CurrentDirectory";
}

[Collection(CurrentDirectoryCollection.Name)]
public sealed class EnvironmentContextMiddlewareTests
{
    [Fact]
    public void SerializeToXml_IncludesEnvironmentContext()
    {
        var context = new EnvironmentContext
        {
            Cwd = "/tmp/repo",
            Shell = "zsh",
            ShellExecutable = "/bin/zsh",
            ShellKind = "posix",
            ShellCommandArgumentsPrefix = ["-lc"],
            AvailableShells =
            [
                new DetectedShell("zsh", "/bin/zsh", "posix", "SHELL", Available: true, Selected: true),
                new DetectedShell("bash", "/bin/bash", "posix", "well_known", Available: true, Selected: false)
            ],
            CurrentDate = "2026-05-11",
            Timezone = "America/Chicago",
            OperatingSystem = "darwin",
            OsVersion = "Darwin 25.3.0",
            IsWindows = false,
            DirectorySeparator = "/",
            AltDirectorySeparator = "/",
            PathSeparator = ":",
            IsGitRepository = true,
            WorkspaceRoot = "/tmp/repo",
            TempDirectory = "/tmp/"
        };

        var xml = context.SerializeToXml();

        xml.Should().Contain("<environment_context>");
        xml.Should().NotContain("<cwd>");
        xml.Should().Contain("<shell>zsh</shell>");
        xml.Should().Contain("<shell_executable>/bin/zsh</shell_executable>");
        xml.Should().Contain("<shell_kind>posix</shell_kind>");
        xml.Should().Contain("<shell_command_arguments>");
        xml.Should().Contain("<arg>-lc</arg>");
        xml.Should().Contain("<current_date>2026-05-11</current_date>");
        xml.Should().Contain("<timezone>America/Chicago</timezone>");
        xml.Should().Contain("<operating_system>darwin</operating_system>");
        xml.Should().Contain("<os_version>Darwin 25.3.0</os_version>");
        xml.Should().Contain("<is_windows>false</is_windows>");
        xml.Should().Contain("<directory_separator>/</directory_separator>");
        xml.Should().Contain("<alt_directory_separator>/</alt_directory_separator>");
        xml.Should().Contain("<path_separator>:</path_separator>");
        xml.Should().Contain("<is_git_repository>true</is_git_repository>");
        xml.Should().Contain("<workspace_root>/tmp/repo</workspace_root>");
        xml.Should().Contain("<temp_directory>/tmp/</temp_directory>");
        xml.Should().Contain("<available_shells>");
        xml.Should().Contain("<shell name=\"zsh\" executable=\"/bin/zsh\" kind=\"posix\" source=\"SHELL\" available=\"true\" selected=\"true\" />");
        xml.Should().Contain("<shell name=\"bash\" executable=\"/bin/bash\" kind=\"posix\" source=\"well_known\" available=\"true\" selected=\"false\" />");
        xml.Should().Contain("</environment_context>");
        xml.Should().NotContain("writable_roots");
        xml.Should().NotContain("Directory Structure:");
    }

    [Fact]
    public void SerializeToXml_EscapesXmlValues()
    {
        var context = new EnvironmentContext
        {
            Cwd = "/tmp/<repo>&\"'",
            Shell = "zsh&bash",
            ShellExecutable = "/bin/<zsh>&",
            ShellKind = "posix&shell",
            ShellCommandArgumentsPrefix = ["-<lc>", "&arg"],
            AvailableShells =
            [
                new DetectedShell("zsh&bash", "/bin/<zsh>&", "posix&shell", "SHELL&env", Available: true, Selected: true)
            ],
            CurrentDate = "2026-05-<11>",
            Timezone = "America/<Chicago>",
            OperatingSystem = "darwin&linux",
            OsVersion = "Darwin <25>&",
            DirectorySeparator = "/",
            AltDirectorySeparator = "\\",
            PathSeparator = ":",
            WorkspaceRoot = "/tmp/<repo>",
            TempDirectory = "/tmp/<temp>"
        };

        var xml = context.SerializeToXml();

        xml.Should().NotContain("<cwd>");
        xml.Should().Contain("<shell>zsh&amp;bash</shell>");
        xml.Should().Contain("<shell_executable>/bin/&lt;zsh&gt;&amp;</shell_executable>");
        xml.Should().Contain("<shell_kind>posix&amp;shell</shell_kind>");
        xml.Should().Contain("<arg>-&lt;lc&gt;</arg>");
        xml.Should().Contain("<arg>&amp;arg</arg>");
        xml.Should().Contain("<current_date>2026-05-&lt;11&gt;</current_date>");
        xml.Should().Contain("<timezone>America/&lt;Chicago&gt;</timezone>");
        xml.Should().Contain("<operating_system>darwin&amp;linux</operating_system>");
        xml.Should().Contain("<os_version>Darwin &lt;25&gt;&amp;</os_version>");
        xml.Should().Contain("<shell name=\"zsh&amp;bash\" executable=\"/bin/&lt;zsh&gt;&amp;\" kind=\"posix&amp;shell\" source=\"SHELL&amp;env\" available=\"true\" selected=\"true\" />");
        xml.Should().Contain("<workspace_root>/tmp/&lt;repo&gt;</workspace_root>");
        xml.Should().Contain("<temp_directory>/tmp/&lt;temp&gt;</temp_directory>");
    }

    [Fact]
    public void SerializeToXml_ReportsSelectedWorkspaceWithoutCwd()
    {
        var context = new EnvironmentContext
        {
            Cwd = "/tmp/source-host",
            WorkspaceRoot = "/tmp/source-host",
            Shell = "zsh",
            ShellExecutable = "/bin/zsh",
            ShellKind = "posix",
            ShellCommandArgumentsPrefix = ["-lc"],
            AvailableShells = [],
            CurrentDate = "2026-05-11",
            Timezone = "America/Chicago",
            OperatingSystem = "darwin",
            OsVersion = "Darwin 25.3.0",
            DirectorySeparator = "/",
            AltDirectorySeparator = "/",
            PathSeparator = ":",
            TempDirectory = "/tmp/"
        };
        var workspace = new AgentWorkspace(
            "default",
            "/tmp/selected-workspace",
            [new AgentWorkspaceRoot("default", "/tmp/selected-workspace")]);

        var xml = context.SerializeToXml(workspace);

        xml.Should().NotContain("<cwd>");
        xml.Should().Contain("<workspace_root>/tmp/selected-workspace</workspace_root>");
        xml.Should().NotContain("<host_process_cwd>");
        xml.Should().Contain("<default_root_path>/tmp/selected-workspace</default_root_path>");
    }

    [Fact]
    public void CreateCurrent_UsesShellOverride()
    {
        var context = EnvironmentContext.CreateCurrent(new EnvironmentContextConfig
        {
            ShellExecutableOverride = "/custom/pwsh",
            ShellKindOverride = "powershell",
            ShellCommandArgumentsPrefixOverride = ["-NoProfile", "-Command"]
        });

        context.Shell.Should().Be("pwsh");
        context.ShellExecutable.Should().Be("/custom/pwsh");
        context.ShellKind.Should().Be("powershell");
        context.ShellCommandArgumentsPrefix.Should().Equal("-NoProfile", "-Command");
        context.AvailableShells.Should().ContainSingle(shell =>
            shell.Executable == "/custom/pwsh" &&
            shell.Source == "config" &&
            shell.Selected);
    }

    [Fact]
    public void CreateCurrent_TreatsGitFileAsRepositoryMarker()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-coding-toolharness-gitfile-{Guid.NewGuid():N}");
        var child = Path.Combine(tempRoot, "src");

        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(tempRoot, ".git"), "gitdir: /tmp/worktree/.git");

        try
        {
            Directory.SetCurrentDirectory(child);
            var resolvedTempRoot = Directory.GetParent(Directory.GetCurrentDirectory())!.FullName;

            var context = EnvironmentContext.CreateCurrent();

            context.IsGitRepository.Should().BeTrue();
            context.WorkspaceRoot.Should().Be(resolvedTempRoot);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BeforeIterationAsync_InsertsEnvironmentContextAfterSystemMessages()
    {
        var middleware = new EnvironmentContextMiddleware(new EnvironmentContextConfig());
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system one"),
            new(ChatRole.System, "system two"),
            new(ChatRole.User, "hello")
        };

        var context = CreateBeforeIterationContext(messages);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        messages.Should().HaveCount(3);
        messages[2].Text.Should().Be("hello");
        context.Options.Instructions.Should().Contain("<environment_context>");
        context.Options.Instructions.Should().Contain("<workspace_root>");
        context.Options.Instructions.Should().NotContain("<cwd>");
    }

    [Fact]
    public async Task BeforeIterationAsync_ReportsSelectedWorkspaceWithoutCwd()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-coding-workspace-context-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(tempRoot, "source");
        var selectedWorkspace = Path.Combine(tempRoot, "workspace");

        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(selectedWorkspace);

        try
        {
            Directory.SetCurrentDirectory(sourceDirectory);
            var hostProcessCwd = Directory.GetCurrentDirectory();
            var middleware = new EnvironmentContextMiddleware(new EnvironmentContextConfig());
            var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
            var context = CreateBeforeIterationContext(
                CreateAgentContext(),
                messages,
                new AgentRunConfig
                {
                    Context = new AgentContextRunConfig { Properties = new Dictionary<string, object>
                    {
                        [AgentWorkspace.ContextKey] = new AgentWorkspace(
                            "default",
                            selectedWorkspace,
                            [new AgentWorkspaceRoot("default", selectedWorkspace)])
                    } }
                });

            await middleware.BeforeIterationAsync(context, CancellationToken.None);

            context.Options.Instructions.Should().NotContain("<cwd>");
            context.Options.Instructions.Should().Contain($"<workspace_root>{selectedWorkspace}</workspace_root>");
            context.Options.Instructions.Should().NotContain("<host_process_cwd>");
            context.Options.Instructions.Should().Contain($"<default_root_path>{selectedWorkspace}</default_root_path>");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BeforeIterationAsync_DoesNotInjectAgainWhenCwdIsUnchanged()
    {
        var middleware = new EnvironmentContextMiddleware(new EnvironmentContextConfig());
        var agentContext = CreateAgentContext();

        var firstMessages = new List<ChatMessage> { new(ChatRole.User, "first") };
        var firstContext = CreateBeforeIterationContext(agentContext, firstMessages);
        await middleware.BeforeIterationAsync(firstContext, CancellationToken.None);

        var secondMessages = new List<ChatMessage> { new(ChatRole.User, "second") };
        var secondContext = CreateBeforeIterationContext(agentContext, secondMessages);
        await middleware.BeforeIterationAsync(secondContext, CancellationToken.None);

        firstMessages.Should().ContainSingle();
        firstContext.Options.Instructions.Should().Contain("<environment_context>");
        secondMessages.Should().ContainSingle();
        secondMessages[0].Text.Should().Be("second");
        secondContext.Options.Instructions.Should().BeNull();
    }

    [Fact]
    public async Task BeforeIterationAsync_ReinjectsWhenCwdChanges()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-coding-toolharness-tests-{Guid.NewGuid():N}");
        var firstDirectory = Path.Combine(tempRoot, "first");
        var secondDirectory = Path.Combine(tempRoot, "second");

        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);

        try
        {
            var middleware = new EnvironmentContextMiddleware(new EnvironmentContextConfig());
            var agentContext = CreateAgentContext();

            Directory.SetCurrentDirectory(firstDirectory);
            var firstCwd = Directory.GetCurrentDirectory();
            var firstMessages = new List<ChatMessage> { new(ChatRole.User, "first") };
            var firstContext = CreateBeforeIterationContext(agentContext, firstMessages);
            await middleware.BeforeIterationAsync(firstContext, CancellationToken.None);

            Directory.SetCurrentDirectory(secondDirectory);
            var secondCwd = Directory.GetCurrentDirectory();
            var secondMessages = new List<ChatMessage> { new(ChatRole.User, "second") };
            var secondContext = CreateBeforeIterationContext(agentContext, secondMessages);
            await middleware.BeforeIterationAsync(secondContext, CancellationToken.None);

            firstContext.Options.Instructions.Should().Contain($"<workspace_root>{firstCwd}</workspace_root>");
            secondContext.Options.Instructions.Should().Contain($"<workspace_root>{secondCwd}</workspace_root>");
            firstContext.Options.Instructions.Should().NotContain("<cwd>");
            secondContext.Options.Instructions.Should().NotContain("<cwd>");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BeforeIterationAsync_StoresLastContextInMiddlewareState()
    {
        var middleware = new EnvironmentContextMiddleware(new EnvironmentContextConfig());
        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
        var context = CreateBeforeIterationContext(messages);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        var state = context.GetMiddlewareState<EnvironmentContextState>();
        state.Should().NotBeNull();
        state!.LastContext.Should().NotBeNull();
        state.LastContext!.Cwd.Should().Be(Directory.GetCurrentDirectory());
    }

    private static BeforeIterationContext CreateBeforeIterationContext(List<ChatMessage> messages)
        => CreateBeforeIterationContext(CreateAgentContext(), messages);

    private static BeforeIterationContext CreateBeforeIterationContext(AgentContext agentContext, List<ChatMessage> messages)
        => CreateBeforeIterationContext(agentContext, messages, new AgentRunConfig());

    private static BeforeIterationContext CreateBeforeIterationContext(
        AgentContext agentContext,
        List<ChatMessage> messages,
        AgentRunConfig runConfig)
        => agentContext.AsBeforeIteration(
            iteration: 0,
            messages,
            new ChatOptions(),
            runConfig);

    private static AgentContext CreateAgentContext()
    {
        var state = AgentLoopState.InitialSafe(
            [],
            "test-run",
            "test-conversation",
            "test-agent");

        var session = new Session("test-session");
        var thread = new Thread("test-session", "test-agent");

        var agentContext = new AgentContext(
            "test-agent",
            "test-conversation",
            state,
            new EventCoordinator(),
            session,
            thread,
            CancellationToken.None);

        return agentContext;
    }
}
