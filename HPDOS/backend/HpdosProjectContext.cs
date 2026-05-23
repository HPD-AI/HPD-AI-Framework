using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record HpdosProjectContext(
    string ProjectId,
    string Directory,
    string Worktree,
    string Path,
    string Name)
{
    private static readonly Regex SlugInvalidCharacters = new("[^a-z0-9_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static HpdosProjectContext Resolve(IConfiguration configuration, string backendDirectory)
    {
        var configuredDirectory = configuration["HPDOS:ProjectDirectory"];
        var directory = NormalizeDirectory(string.IsNullOrWhiteSpace(configuredDirectory) ? backendDirectory : configuredDirectory);
        var worktree = FindGitWorktree(directory) ?? directory;
        var relativePath = System.IO.Path.GetRelativePath(worktree, directory);
        if (relativePath == ".")
            relativePath = "";

        var configuredName = configuration["HPDOS:ProjectName"];
        var name = string.IsNullOrWhiteSpace(configuredName)
            ? new DirectoryInfo(string.IsNullOrWhiteSpace(relativePath) ? worktree : directory).Name
            : configuredName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "HPD-OS";

        return new HpdosProjectContext(
            ProjectId: StableProjectId(worktree),
            Directory: directory,
            Worktree: worktree,
            Path: relativePath,
            Name: name);
    }

    private static string NormalizeDirectory(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path.Trim());
        if (!System.IO.Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Project directory does not exist: {fullPath}");

        return fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private static string? FindGitWorktree(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(current.FullName, ".git"))
                || File.Exists(System.IO.Path.Combine(current.FullName, ".git")))
            {
                return current.FullName.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }

            current = current.Parent;
        }

        return null;
    }

    private static string StableProjectId(string worktree)
    {
        var name = new DirectoryInfo(worktree).Name;
        var slug = Slug(name);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(worktree)))
            .ToLowerInvariant()[..10];
        return $"{slug}-{hash}";
    }

    private static string Slug(string value)
    {
        var slug = SlugInvalidCharacters
            .Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "project" : slug;
    }
}
