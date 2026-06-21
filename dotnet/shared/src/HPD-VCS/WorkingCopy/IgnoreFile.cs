using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using HPD.VCS.Core;

namespace HPD.VCS.WorkingCopy;

/// <summary>
/// Represents a single ignore rule pattern with its base directory context.
/// </summary>
public readonly record struct IgnoreRule
{
    /// <summary>
    /// The pattern string for this ignore rule.
    /// </summary>
    public string PatternString { get; init; }
    
    /// <summary>
    /// The base directory where this rule is defined (relative to repository root).
    /// </summary>
    public RepoPath BaseDir { get; init; }

    /// <summary>
    /// Creates a new ignore rule with the specified pattern and base directory.
    /// </summary>
    /// <param name="pattern">The pattern string (e.g., "*.txt", "bin/", "/root")</param>
    /// <param name="baseDir">The base directory where this rule is defined</param>
    public IgnoreRule(string pattern, RepoPath baseDir)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        PatternString = pattern.Trim();
        BaseDir = baseDir;
    }    /// <summary>
    /// Checks if the specified file path matches this ignore rule.
    /// </summary>
    /// <param name="filePathToCheck">The repository-relative file path to check</param>
    /// <param name="isDirectory">True if the path represents a directory</param>
    /// <returns>True if the path matches this ignore rule</returns>
    public bool IsMatch(RepoPath filePathToCheck, bool isDirectory)
    {
        if (string.IsNullOrEmpty(PatternString))
            return false;

        var pattern = PatternString;
        
        // Handle negation patterns (starting with !)
        if (pattern.StartsWith('!'))
        {
            return !new IgnoreRule(pattern[1..], BaseDir)
                     .IsMatch(filePathToCheck, isDirectory);
        }
        
        // Handle rooted patterns (starting with /)
        if (pattern.StartsWith('/'))
        {
            pattern = pattern.Substring(1);
            return IsMatchFromBase(filePathToCheck, pattern, isDirectory);
        }
        
        // Handle directory patterns (ending with /)
        if (pattern.EndsWith('/'))
        {
            pattern = pattern.Substring(0, pattern.Length - 1);
            return IsDirectoryMatch(filePathToCheck, pattern, isDirectory);
        }
        
        // Handle wildcard patterns
        if (pattern.Contains('*'))
        {
            return IsWildcardMatch(filePathToCheck, pattern);
        }
        
        // Handle exact name matches
        return IsExactNameMatch(filePathToCheck, pattern);
    }    /// <summary>
    /// Checks if a path matches when rooted from the base directory.
    /// </summary>
    private bool IsMatchFromBase(RepoPath filePathToCheck, string pattern, bool isDirectory)
    {
        // For rooted patterns, we need to check if the file path starts with pattern from base
        var filePathString = (string)filePathToCheck;
        var baseDirString = (string)BaseDir;
        
        // Calculate what the file path would be relative to base
        string relativePathString;
        if (string.IsNullOrEmpty(baseDirString))
        {
            // Base is root, so relative path is the full path
            relativePathString = filePathString;
        }
        else
        {
            // Check if file path starts with base directory - strict prefix matching
            if (!filePathString.StartsWith(baseDirString + "/") && filePathString != baseDirString)
            {
                return false;
            }
            
            // Get the relative path
            if (filePathString == baseDirString)
            {
                relativePathString = "";
            }
            else
            {
                relativePathString = filePathString.Substring(baseDirString.Length + 1);
            }
        }
        
        // Handle directory patterns
        if (pattern.EndsWith('/'))
        {
            var dirPattern = pattern.Substring(0, pattern.Length - 1);
            return isDirectory && 
                   (relativePathString == dirPattern || 
                    relativePathString.StartsWith(dirPattern + "/"));
        }
        
        // Handle wildcard patterns
        if (pattern.Contains('*'))
        {
            // For rooted patterns with wildcards, we need an exact match of the relative path
            return IsSimpleWildcardMatch(relativePathString, pattern);
        }
        
        // For rooted patterns, we need exact match or direct child match 
        return relativePathString == pattern || relativePathString.StartsWith(pattern + "/");
    }

    /// <summary>
    /// Checks if a directory pattern matches.
    /// </summary>
    private bool IsDirectoryMatch(RepoPath filePathToCheck, string pattern, bool isDirectory)
    {
        // Directory patterns only match directories and their contents
        if (!isDirectory)
        {
            // Check if this file is inside a matching directory
            var parentPath = filePathToCheck.Parent();
            while (parentPath != null)
            {
                if (IsExactNameMatch(parentPath.Value, pattern))
                    return true;
                parentPath = parentPath.Value.Parent();
            }
            return false;
        }
        
        // For directories, check if any component matches the pattern
        return IsExactNameMatch(filePathToCheck, pattern);
    }    /// <summary>
    /// Checks if a wildcard pattern matches.
    /// </summary>
    private bool IsWildcardMatch(RepoPath filePathToCheck, string pattern)
    {
        // Check against the filename
        var fileName = filePathToCheck.FileName();
        if (fileName != null && IsSimpleWildcardMatch(fileName.Value, pattern))
            return true;
        
        // Check against any path component
        foreach (var component in filePathToCheck.Components)
        {
            if (IsSimpleWildcardMatch(component.Value, pattern))
                return true;
        }
        
        // Special case for *substring* patterns - check if any component contains the substring
        if (pattern.StartsWith("*") && pattern.EndsWith("*") && pattern.Length > 2)
        {
            var substring = pattern.Substring(1, pattern.Length - 2);
            if (string.IsNullOrEmpty(substring))
                return false; // Empty substring means no match
            
            // Check if any component contains the substring
            foreach (var component in filePathToCheck.Components)
            {
                string componentValue = component.Value;
                if (componentValue.Contains(substring))
                    return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Checks if an exact name pattern matches.
    /// </summary>
    private bool IsExactNameMatch(RepoPath filePathToCheck, string pattern)
    {
        // Check against the filename
        var fileName = filePathToCheck.FileName();
        if (fileName != null && fileName.Value == pattern)
            return true;
        
        // Check against any path component
        foreach (var component in filePathToCheck.Components)
        {
            if (component.Value == pattern)
                return true;
        }
        
        return false;
    }    /// <summary>
    /// Performs simple wildcard matching (* only).
    /// </summary>
    private static bool IsSimpleWildcardMatch(string text, string pattern)
    {
        if (!pattern.Contains('*'))
            return text == pattern;
        
        // Handle patterns like *.ext
        if (pattern.StartsWith("*."))
        {
            var extension = pattern.Substring(2);
            return text.EndsWith("." + extension);
        }
        
        // Handle patterns like name*
        if (pattern.EndsWith("*") && !pattern.StartsWith("*"))
        {
            var prefix = pattern.Substring(0, pattern.Length - 1);
            return text.StartsWith(prefix);
        }
        
        // Handle patterns like *name
        if (pattern.StartsWith("*") && !pattern.EndsWith("*"))
        {
            var suffix = pattern.Substring(1);
            return text.EndsWith(suffix);
        }
        
        // Handle patterns like *name*
        if (pattern.StartsWith("*") && pattern.EndsWith("*"))
        {
            var substring = pattern.Substring(1, pattern.Length - 2);
            return text.Contains(substring);
        }
        
        // For more complex patterns, fall back to sequential matching
        var parts = pattern.Split('*', StringSplitOptions.None);
        if (parts.Length == 0)
            return true; // Pattern is just "*"
        
        var currentIndex = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            
            if (string.IsNullOrEmpty(part))
            {
                // Empty part means we had consecutive * or * at start/end
                continue;
            }
            
            if (i == 0 && !pattern.StartsWith("*"))
            {
                // First part must match at the beginning
                if (!text.StartsWith(part))
                    return false;
                currentIndex = part.Length;
            }
            else if (i == parts.Length - 1 && !pattern.EndsWith("*"))
            {
                // Last part must match at the end
                if (!text.EndsWith(part))
                    return false;
            }
            else
            {
                // Middle parts must exist in sequence
                var index = text.IndexOf(part, currentIndex);
                if (index == -1)
                    return false;
                currentIndex = index + part.Length;
            }
        }
        
        return true;
    }

    /// <summary>
    /// Returns a string representation of this ignore rule.
    /// </summary>
    public override string ToString()
    {
        return $"IgnoreRule(\"{PatternString}\" in {BaseDir})";
    }
}

/// <summary>
/// Represents a collection of ignore rules loaded from a .hpdignore file.
/// </summary>
public class IgnoreFile
{
    /// <summary>
    /// Gets the list of ignore rules in this file.
    /// </summary>
    public IReadOnlyList<IgnoreRule> Rules { get; }

    /// <summary>
    /// Creates a new IgnoreFile with the specified rules.
    /// </summary>
    /// <param name="rules">The ignore rules</param>
    public IgnoreFile(IEnumerable<IgnoreRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = rules.ToList().AsReadOnly();
    }

    /// <summary>
    /// Creates an empty IgnoreFile with no rules.
    /// </summary>
    public IgnoreFile() : this(Enumerable.Empty<IgnoreRule>())
    {
    }

    /// <summary>
    /// Loads an ignore file from disk.
    /// </summary>
    /// <param name="fs">The file system abstraction</param>
    /// <param name="diskFilePath">The disk path to the ignore file</param>
    /// <param name="baseDirInRepo">The repository-relative base directory</param>
    /// <returns>An IgnoreFile with the loaded rules, or empty if file not found</returns>
    public static IgnoreFile Load(IFileSystem fs, string diskFilePath, RepoPath baseDirInRepo)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(diskFilePath);

        if (!fs.File.Exists(diskFilePath))
        {
            return new IgnoreFile();
        }

        try
        {
            var lines = fs.File.ReadAllLines(diskFilePath);
            var rules = new List<IgnoreRule>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                    continue;                rules.Add(new IgnoreRule(trimmedLine, baseDirInRepo));
            }
            
            return new IgnoreFile(rules);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // If we can't read the file, return empty ignore file
            return new IgnoreFile();
        }
    }

    /// <summary>
    /// Loads an ignore file from disk asynchronously.
    /// </summary>
    /// <param name="fs">The file system abstraction</param>
    /// <param name="diskFilePath">The disk path to the ignore file</param>
    /// <param name="baseDirInRepo">The repository-relative base directory</param>
    /// <returns>A task that resolves to an IgnoreFile with the loaded rules, or empty if file not found</returns>
    public static Task<IgnoreFile> LoadAsync(
        IFileSystem fs, string diskFilePath, RepoPath baseDirInRepo) =>
        Task.FromResult(Load(fs, diskFilePath, baseDirInRepo));

    /// <summary>
    /// Checks if the specified file path matches any ignore rule in this file.
    /// </summary>
    /// <param name="filePathToCheck">The repository-relative file path to check</param>
    /// <param name="isDirectory">True if the path represents a directory</param>
    /// <returns>True if the path should be ignored</returns>
    public bool IsMatch(RepoPath filePathToCheck, bool isDirectory)
    {
        // Last matching rule wins (precedence)
        bool shouldIgnore = false;
        
        foreach (var rule in Rules)
        {
            if (rule.IsMatch(filePathToCheck, isDirectory))
            {
                shouldIgnore = true;
            }
        }
        
        return shouldIgnore;
    }

    /// <summary>
    /// Combines this ignore file with a dominant ignore file.
    /// Rules from the dominant file take precedence (are checked last).
    /// </summary>
    /// <param name="dominantIgnoreFile">The ignore file that should take precedence</param>
    /// <returns>A new IgnoreFile with combined rules</returns>
    public IgnoreFile CombineWith(IgnoreFile dominantIgnoreFile)
    {
        ArgumentNullException.ThrowIfNull(dominantIgnoreFile);
        
        // Rules from this file first, then rules from dominant file
        // This means dominant rules are checked last and thus take precedence
        var combinedRules = Rules.Concat(dominantIgnoreFile.Rules);
        return new IgnoreFile(combinedRules);
    }

    /// <summary>
    /// Returns a string representation of this ignore file.
    /// </summary>
    public override string ToString()
    {
        return $"IgnoreFile({Rules.Count} rules)";
    }
}
