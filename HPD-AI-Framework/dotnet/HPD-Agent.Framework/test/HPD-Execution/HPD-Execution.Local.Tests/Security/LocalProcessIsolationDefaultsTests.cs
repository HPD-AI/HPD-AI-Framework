using HPD.Execution.Local.Security;
using Xunit;

namespace HPD.Execution.Local.Tests.Security;

public class LocalProcessIsolationDefaultsTests
{
    [Fact]
    public void DangerousFiles_ContainsGitConfig()
    {
        Assert.Contains(".gitconfig", LocalProcessIsolationDefaults.DangerousFiles);
    }

    [Fact]
    public void DangerousFiles_ContainsBashrc()
    {
        Assert.Contains(".bashrc", LocalProcessIsolationDefaults.DangerousFiles);
    }

    [Fact]
    public void DangerousFiles_ContainsZshrc()
    {
        Assert.Contains(".zshrc", LocalProcessIsolationDefaults.DangerousFiles);
    }

    [Fact]
    public void DangerousDirectories_ContainsGitHooks()
    {
        Assert.Contains(".git/hooks", LocalProcessIsolationDefaults.DangerousDirectories);
    }

    [Fact]
    public void DangerousDirectories_ContainsVscode()
    {
        Assert.Contains(".vscode", LocalProcessIsolationDefaults.DangerousDirectories);
    }

    [Fact]
    public void SensitiveDirectories_ContainsSsh()
    {
        Assert.Contains("~/.ssh", LocalProcessIsolationDefaults.SensitiveDirectories);
    }

    [Fact]
    public void SensitiveDirectories_ContainsAws()
    {
        Assert.Contains("~/.aws", LocalProcessIsolationDefaults.SensitiveDirectories);
    }

    [Fact]
    public void DefaultWritePaths_ContainsTmp()
    {
        Assert.Contains("/tmp", LocalProcessIsolationDefaults.DefaultWritePaths);
    }

    [Fact]
    public void SafeEnvironmentVariables_ContainsPath()
    {
        Assert.Contains("PATH", LocalProcessIsolationDefaults.SafeEnvironmentVariables);
    }

    [Fact]
    public void SafeEnvironmentVariables_ContainsHome()
    {
        Assert.Contains("HOME", LocalProcessIsolationDefaults.SafeEnvironmentVariables);
    }
}
