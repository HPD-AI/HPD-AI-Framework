using System;
using System.IO;
using System.Collections.Generic;
using MAB.DotIgnore;

namespace HPD.Agent.Toolkit.FileSystem;

/// <summary>
/// Checks if files/directories should be ignored based on .gitignore and .hpdignore patterns.
/// Uses MAB.DotIgnore library for spec-compliant .gitignore parsing.
/// </summary>
public class GitIgnoreChecker
{
    private readonly IgnoreList _ignoreList;
    private readonly string _workspaceRoot;
    private readonly bool _respectGitIgnore;
    private readonly bool _respectHpdIgnore;

    /// <summary>
    /// Creates a GitIgnoreChecker for the specified workspace
    /// </summary>
    /// <param name="workspaceRoot">Root directory to check for ignore files</param>
    /// <param name="respectGitIgnore">Whether to respect .gitignore patterns</param>
    /// <param name="respectHpdIgnore">Whether to respect .hpdignore patterns (custom ignore file)</param>
    public GitIgnoreChecker(string workspaceRoot, bool respectGitIgnore = true, bool respectHpdIgnore = true)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _respectGitIgnore = respectGitIgnore;
        _respectHpdIgnore = respectHpdIgnore;

        // Create empty IgnoreList - we'll add rules manually
        _ignoreList = new IgnoreList(Array.Empty<string>());

        LoadIgnoreFiles();
    }

    /// <summary>
    /// Checks if a file or directory path should be ignored
    /// </summary>
    /// <param name="path">Absolute or relative path to check</param>
    /// <returns>True if the path should be ignored, false otherwise</returns>
    public bool IsIgnored(string path)
    {
        // Convert to relative path from workspace root
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_workspaceRoot, path);
        var relativePath = Path.GetRelativePath(_workspaceRoot, fullPath);

        // Normalize path separators to forward slashes (gitignore standard)
        relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');

        // MAB.DotIgnore expects a FileInfo object
        var fileInfo = new FileInfo(fullPath);
        return _ignoreList.IsIgnored(fileInfo);
    }

    /// <summary>
    /// Filters a list of paths, removing ignored ones
    /// </summary>
    public IEnumerable<string> FilterIgnored(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!IsIgnored(path))
                yield return path;
        }
    }

    /// <summary>
    /// Gets the count of paths that would be ignored from a list
    /// </summary>
    public int CountIgnored(IEnumerable<string> paths)
    {
        int count = 0;
        foreach (var path in paths)
        {
            if (IsIgnored(path))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Loads ignore patterns from .gitignore and .hpdignore files
    /// </summary>
    private void LoadIgnoreFiles()
    {
        // Load .gitignore if it exists and is enabled
        if (_respectGitIgnore)
        {
            var gitignorePath = Path.Combine(_workspaceRoot, ".gitignore");
            if (File.Exists(gitignorePath))
            {
                try
                {
                    _ignoreList.AddRules(File.ReadAllLines(gitignorePath));
                }
                catch
                {
                    // Ignore errors loading .gitignore
                }
            }
        }

        // Load .hpdignore (custom ignore file) if it exists and is enabled
        if (_respectHpdIgnore)
        {
            var hpdignorePath = Path.Combine(_workspaceRoot, ".hpdignore");
            if (File.Exists(hpdignorePath))
            {
                try
                {
                    _ignoreList.AddRules(File.ReadAllLines(hpdignorePath));
                }
                catch
                {
                    // Ignore errors loading .hpdignore
                }
            }
        }

        // Always add common ignore patterns (safety defaults)
        _ignoreList.AddRules(new[]
        {
            ".git/",           // Git metadata
            ".svn/",           // SVN metadata
            ".hg/",            // Mercurial metadata
            "node_modules/",   // Node.js dependencies
            "__pycache__/",    // Python cache
            "*.pyc",           // Python compiled
            ".DS_Store",       // macOS metadata
            "Thumbs.db",       // Windows thumbnails
        });
    }
}
