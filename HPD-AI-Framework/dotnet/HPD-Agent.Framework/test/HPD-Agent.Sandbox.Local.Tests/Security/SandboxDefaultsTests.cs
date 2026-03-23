using HPD.Sandbox.Local.Security;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Security;

public class SandboxDefaultsTests
{
    [Fact]
    public void DangerousFiles_ContainsGitConfig()
    {
        Assert.Contains(".gitconfig", SandboxDefaults.DangerousFiles);
    }

    [Fact]
    public void DangerousFiles_ContainsBashrc()
    {
        Assert.Contains(".bashrc", SandboxDefaults.DangerousFiles);
    }

    [Fact]
    public void DangerousFiles_ContainsZshrc()
    {
        Assert.Contains(".zshrc", SandboxDefaults.DangerousFiles);
    }

    [Fact]
    public void DangerousDirectories_ContainsGitHooks()
    {
        Assert.Contains(".git/hooks", SandboxDefaults.DangerousDirectories);
    }

    [Fact]
    public void DangerousDirectories_ContainsVscode()
    {
        Assert.Contains(".vscode", SandboxDefaults.DangerousDirectories);
    }

    [Fact]
    public void SensitiveDirectories_ContainsSsh()
    {
        Assert.Contains("~/.ssh", SandboxDefaults.SensitiveDirectories);
    }

    [Fact]
    public void SensitiveDirectories_ContainsAws()
    {
        Assert.Contains("~/.aws", SandboxDefaults.SensitiveDirectories);
    }

    [Fact]
    public void DefaultWritePaths_ContainsTmp()
    {
        Assert.Contains("/tmp", SandboxDefaults.DefaultWritePaths);
    }

    [Fact]
    public void SafeEnvironmentVariables_ContainsPath()
    {
        Assert.Contains("PATH", SandboxDefaults.SafeEnvironmentVariables);
    }

    [Fact]
    public void SafeEnvironmentVariables_ContainsHome()
    {
        Assert.Contains("HOME", SandboxDefaults.SafeEnvironmentVariables);
    }
}
