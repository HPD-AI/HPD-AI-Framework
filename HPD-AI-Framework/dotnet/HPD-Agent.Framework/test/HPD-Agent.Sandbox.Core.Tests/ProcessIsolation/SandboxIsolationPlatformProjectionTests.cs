namespace HPD.Agent.Sandbox.Tests.ProcessIsolation;

using FluentAssertions;
using HPD.Execution.Contracts;
using HPD.Agent.Sandbox.Platforms.Linux;
using HPD.Agent.Sandbox.Platforms.MacOS;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Agent.Sandbox.Security;
using Xunit;

public sealed class SandboxIsolationPlatformProjectionTests
{
    [Fact]
    public void Bubblewrap_builder_accepts_local_filesystem_plan_directly()
    {
        using var temp = TestFilesystem.Create();
        SandboxFilesystemIsolationPlan filesystem = FilesystemPlan(temp);
        var builder = new BubblewrapBuilder();

        builder.WithFilesystemPlan(filesystem, mandatoryDenyWritePaths: [temp.EnvFile]);

        string rendered = builder.Build("/bin/true");

        rendered.Should().Contain($"'--bind' '{temp.Workspace}' '{temp.Workspace}'");
        rendered.Should().Contain($"'--tmpfs' '{temp.SshDirectory}'");
        rendered.Should().Contain($"'--ro-bind' '{temp.GitHooksDirectory}' '{temp.GitHooksDirectory}'");
        rendered.Should().Contain($"'--ro-bind' '{temp.EnvFile}' '{temp.EnvFile}'");
    }

    [Fact]
    public void Seatbelt_builder_accepts_local_filesystem_plan_directly()
    {
        using var temp = TestFilesystem.Create();
        SandboxFilesystemIsolationPlan filesystem = FilesystemPlan(temp);
        var builder = new SeatbeltProfileBuilder("test-log-tag");

        string profile = builder
            .WithFilesystemPlan(filesystem, mandatoryDenyWritePaths: [temp.EnvFile])
            .Build();

        profile.Should().Contain($"(subpath \"{temp.Workspace}\")");
        profile.Should().Contain($"(subpath \"{temp.SshDirectory}\")");
        profile.Should().Contain($"(subpath \"{temp.GitHooksDirectory}\")");
        profile.Should().Contain($"(subpath \"{temp.EnvFile}\")");
    }

    private static SandboxFilesystemIsolationPlan FilesystemPlan(TestFilesystem temp) =>
        new()
        {
            Rules =
            [
                new SandboxPathAccessRule(PathAccessRuleKind.AllowWrite, new HostPath(temp.Workspace), PathPatternKind.LiteralOrSubpath, "workspace writes"),
                new SandboxPathAccessRule(PathAccessRuleKind.DenyRead, new HostPath(temp.SshDirectory), PathPatternKind.LiteralOrSubpath, "credential boundary"),
                new SandboxPathAccessRule(PathAccessRuleKind.DenyWrite, new HostPath(temp.GitHooksDirectory), PathPatternKind.LiteralOrSubpath, "hook protection"),
            ],
        };

    private sealed class TestFilesystem : IDisposable
    {
        private TestFilesystem(string root)
        {
            Root = root;
            var workspace = Path.Combine(root, "workspace");
            var sshDirectory = Path.Combine(root, "home", "agent", ".ssh");
            var gitHooksDirectory = Path.Combine(workspace, ".git", "hooks");
            var envFile = Path.Combine(workspace, ".env");

            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(sshDirectory);
            Directory.CreateDirectory(gitHooksDirectory);
            File.WriteAllText(envFile, "SECRET=1");

            Workspace = PathNormalizer.Normalize(workspace, resolveSymlinks: true);
            SshDirectory = PathNormalizer.Normalize(sshDirectory, resolveSymlinks: true);
            GitHooksDirectory = PathNormalizer.Normalize(gitHooksDirectory, resolveSymlinks: true);
            EnvFile = PathNormalizer.Normalize(envFile, resolveSymlinks: true);
        }

        public string Root { get; }
        public string Workspace { get; }
        public string SshDirectory { get; }
        public string GitHooksDirectory { get; }
        public string EnvFile { get; }

        public static TestFilesystem Create() =>
            new(Path.Combine(Path.GetTempPath(), "hpd-local-isolation-" + Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
