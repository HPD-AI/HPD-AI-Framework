using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace HPD.VCS.Core;

/// <summary>
/// Represents a single path component within a repository.
/// Ensures that path components are valid and cannot contain path separators.
/// </summary>
public readonly record struct RepoPathComponent
{
    /// <summary>
    /// The string value of this path component
    /// </summary>
    public string Value { get; init; }

    /// <summary>
    /// Creates a new RepoPathComponent
    /// </summary>
    /// <param name="value">The path component value</param>
    /// <exception cref="ArgumentNullException">Thrown when value is null</exception>
    /// <exception cref="ArgumentException">Thrown when value is invalid</exception>
    public RepoPathComponent(string value)
    {        ArgumentNullException.ThrowIfNull(value);
        
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path component cannot be null, empty, or whitespace", nameof(value));
            
        if (value.Contains('/'))
            throw new ArgumentException("Path component cannot contain '/' characters", nameof(value));
            
        if (value.Contains('\\'))
            throw new ArgumentException("Path component cannot contain '\\' characters", nameof(value));
            
        if (value is "." or "..")
            throw new ArgumentException("Path component cannot be '.' or '..'", nameof(value));

        // Note: Unicode normalization could be added here if needed:
        // Value = value.Normalize(NormalizationForm.FormC);
        // For now, we skip this optimization as paths are mostly constructed internally
        
        Value = value;
    }

    /// <summary>
    /// Returns the string representation of this component
    /// </summary>
    public override string ToString()
    {
        return Value;
    }

    /// <summary>
    /// Implicit conversion from string to RepoPathComponent for convenience
    /// </summary>
    public static implicit operator RepoPathComponent(string value)
    {
        return new RepoPathComponent(value);
    }

    /// <summary>
    /// Implicit conversion from RepoPathComponent to string for convenience
    /// </summary>
    public static implicit operator string(RepoPathComponent component)
    {
        return component.Value;
    }
}

/// <summary>
/// Represents a path within a repository as a sequence of validated components.
/// Provides type-safe path manipulation and ensures canonical representation.
/// </summary>
public readonly record struct RepoPath
{
    /// <summary>
    /// The root path (empty path with no components)
    /// </summary>
    public static RepoPath Root { get; } = new RepoPath(ImmutableArray<RepoPathComponent>.Empty);

    /// <summary>
    /// The components that make up this path.
    /// Uses ImmutableArray for complete immutability and better performance than IReadOnlyList.
    /// </summary>
    public ImmutableArray<RepoPathComponent> Components { get; init; }

    /// <summary>
    /// Creates a new RepoPath from a collection of components
    /// </summary>
    /// <param name="components">The path components</param>
    public RepoPath(IEnumerable<RepoPathComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        Components = components.ToImmutableArray();
    }

    /// <summary>
    /// Creates a new RepoPath from individual components
    /// </summary>
    /// <param name="components">The path components</param>
    public RepoPath(params RepoPathComponent[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        Components = components.ToImmutableArray();
    }

    /// <summary>
    /// Internal constructor for direct ImmutableArray assignment (performance optimization)
    /// </summary>
    private RepoPath(ImmutableArray<RepoPathComponent> components)
    {
        Components = components;
    }    /// <summary>
    /// Gets whether this path represents the root (empty path)
    /// </summary>
    public bool IsRoot => Components.Length == 0;

    /// <summary>
    /// Gets the parent path, or null if this is the root path
    /// </summary>
    /// <returns>The parent path or null if root</returns>
    public RepoPath? Parent()
    {
        if (IsRoot)
            return null;
            
        if (Components.Length == 1)
            return Root;
            
        return new RepoPath(Components.Take(Components.Length - 1));
    }

    /// <summary>
    /// Gets the file name (last component), or null if this is the root path
    /// </summary>
    /// <returns>The last component or null if root</returns>
    public RepoPathComponent? FileName()
    {
        if (IsRoot)
            return null;
            
        return Components[Components.Length - 1];
    }

    /// <summary>
    /// Creates a new path by appending a component to this path
    /// </summary>
    /// <param name="component">The component to append</param>
    /// <returns>A new RepoPath with the component appended</returns>
    public RepoPath Join(RepoPathComponent component)
    {
        return new RepoPath(Components.Add(component));
    }

    /// <summary>
    /// Creates a new path by appending a string component to this path
    /// </summary>
    /// <param name="componentValue">The component value to append</param>
    /// <returns>A new RepoPath with the component appended</returns>
    public RepoPath Join(string componentValue)
    {
        return Join(new RepoPathComponent(componentValue));
    }    /// <summary>
    /// Converts this path to its internal string representation (forward-slash separated)
    /// </summary>
    /// <returns>The path as a string, or empty string for root</returns>
    public string ToInternalString()
    {
        if (IsRoot)
            return "";
            
        return string.Join("/", Components.Select(c => c.Value));
    }

    /// <summary>
    /// Returns the string representation of this path
    /// Uses internal string format (forward-slash separated).
    /// 
    /// Note on root representation: The root path is represented as an empty string ("")
    /// rather than a dot (".") for consistency with most VCS systems, though both are
    /// accepted when parsing via FromInternalString.
    /// </summary>
    public override string ToString()
    {
        if (IsRoot)
            return "";
            
        return ToInternalString();
    }/// <summary>
    /// Parses a forward-slash separated string into a RepoPath
    /// </summary>
    /// <param name="pathString">The path string to parse</param>
    /// <returns>A RepoPath representing the parsed path</returns>
    /// <exception cref="ArgumentNullException">Thrown when pathString is null</exception>
    /// <exception cref="ArgumentException">Thrown when path components are invalid</exception>
    public static RepoPath FromInternalString(string pathString)
    {
        ArgumentNullException.ThrowIfNull(pathString);
        
        // Empty string or just whitespace represents the root path
        if (string.IsNullOrWhiteSpace(pathString))
            return Root;
            
        // Handle single dot as root (alternative representation)
        if (pathString == ".")
            return Root;
            
        // Split by forward slash and create components
        var parts = pathString.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        // Validate and create components
        var components = new List<RepoPathComponent>();
        foreach (var part in parts)
        {
            // This will validate the component via RepoPathComponent constructor
            components.Add(new RepoPathComponent(part));
        }
        
        return new RepoPath(components);
    }

    /// <summary>
    /// Implicit conversion from string to RepoPath for convenience
    /// </summary>
    public static implicit operator RepoPath(string pathString)
    {
        return FromInternalString(pathString);
    }

    /// <summary>
    /// Implicit conversion from RepoPath to string for convenience
    /// </summary>
    public static implicit operator string(RepoPath path)
    {
        return path.ToInternalString();
    }    /// <summary>
    /// Checks if this path starts with the specified prefix path
    /// </summary>
    /// <param name="prefix">The prefix path to check</param>
    /// <returns>True if this path starts with the prefix</returns>
    public bool StartsWith(RepoPath prefix)
    {
        if (prefix.Components.Length > Components.Length)
            return false;
            
        for (int i = 0; i < prefix.Components.Length; i++)
        {
            if (!Components[i].Value.Equals(prefix.Components[i].Value, StringComparison.Ordinal))
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// Checks if this path is an ancestor of the target path (target contains this path as prefix)
    /// </summary>
    /// <param name="target">The potential descendant path</param>
    /// <returns>True if target starts with this path</returns>
    public bool IsAncestorOf(RepoPath target)
    {
        return target.StartsWith(this);
    }
      /// <summary>
    /// Gets the remaining components after removing this path as a prefix from target
    /// </summary>
    /// <param name="target">The target path</param>
    /// <returns>The remaining path components, or null if this is not a prefix of target</returns>
    public RepoPath? GetSuffix(RepoPath target)
    {
        if (!IsAncestorOf(target))
            return null;
            
        var remainingComponents = target.Components.Skip(Components.Length);
        return new RepoPath(remainingComponents);
    }

    /// <summary>
    /// Custom equality implementation for proper ImmutableArray comparison
    /// </summary>
    public bool Equals(RepoPath other)
    {
        return Components.SequenceEqual(other.Components);
    }

    /// <summary>
    /// Custom hash code implementation for proper ImmutableArray hashing
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in Components)
        {
            hash.Add(component);
        }
        return hash.ToHashCode();
    }
}
