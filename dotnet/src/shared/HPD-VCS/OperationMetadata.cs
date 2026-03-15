using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPD.VCS.Core;

/// <summary>
/// Represents metadata associated with an operation in the VCS system.
/// This tracks information about when and how an operation was performed.
/// Based on jj's OperationMetadata but simplified for initial implementation.
/// </summary>
public readonly record struct OperationMetadata : IContentHashable
{
    /// <summary>
    /// The time when the operation started (UTC)
    /// </summary>
    public DateTimeOffset StartTime { get; init; }
    
    /// <summary>
    /// The time when the operation completed (UTC)
    /// </summary>
    public DateTimeOffset EndTime { get; init; }
    
    /// <summary>
    /// Human-readable description of the operation (e.g., "commit", "snapshot working copy")
    /// </summary>
    public string Description { get; init; }
    
    /// <summary>
    /// Username of the user who performed the operation
    /// </summary>
    public string Username { get; init; }
    
    /// <summary>
    /// Hostname of the machine where the operation was performed
    /// </summary>
    public string Hostname { get; init; }
    
    /// <summary>
    /// Additional metadata tags as key-value pairs
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; }

    /// <summary>
    /// Creates a new OperationMetadata with the specified properties
    /// </summary>
    public OperationMetadata(
        DateTimeOffset startTime, 
        DateTimeOffset endTime, 
        string description, 
        string username, 
        string hostname, 
        IReadOnlyDictionary<string, string> tags)
    {
        StartTime = startTime;
        EndTime = endTime;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Username = username ?? throw new ArgumentNullException(nameof(username));
        Hostname = hostname ?? throw new ArgumentNullException(nameof(hostname));
        Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        
        // Validate that strings are reasonable length and UTF-8
        ValidateString(nameof(description), description);
        ValidateString(nameof(username), username);
        ValidateString(nameof(hostname), hostname);
        
        foreach (var (key, value) in tags)
        {
            ValidateString($"tag key '{key}'", key);
            ValidateString($"tag value for '{key}'", value);
        }
    }
    
    private static void ValidateString(string fieldName, string value)
    {
        if (value.Length > 1024) // Reasonable limit
        {
            throw new ArgumentException($"{fieldName} cannot exceed 1024 characters", fieldName);
        }
        
        // Ensure it's valid UTF-8 by attempting to encode
        try
        {
            Encoding.UTF8.GetBytes(value);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"{fieldName} must be valid UTF-8", fieldName, ex);
        }
    }    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Format:
    /// start_time_unix_milliseconds\n
    /// end_time_unix_milliseconds\n
    /// description_length\ndescription_bytes\n
    /// username_length\nusername_bytes\n
    /// hostname_length\nhostname_bytes\n
    /// tags_count\n
    /// tag1_key_length\ntag1_key_bytes\ntag1_value_length\ntag1_value_bytes\n
    /// ...
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        var builder = new StringBuilder();        // Convert times to Unix milliseconds for consistent representation (preserves fractional seconds)
        var startTimeUnix = StartTime.ToUnixTimeMilliseconds();
        var endTimeUnix = EndTime.ToUnixTimeMilliseconds();
        
        builder.Append(startTimeUnix.ToString()).Append('\n');
        builder.Append(endTimeUnix.ToString()).Append('\n');
        
        // Description
        var descriptionBytes = Encoding.UTF8.GetBytes(Description);
        builder.Append(descriptionBytes.Length.ToString()).Append('\n');
        builder.Append(Description);
        builder.Append('\n');
        
        // Username
        var usernameBytes = Encoding.UTF8.GetBytes(Username);
        builder.Append(usernameBytes.Length.ToString()).Append('\n');
        builder.Append(Username);
        builder.Append('\n');
        
        // Hostname
        var hostnameBytes = Encoding.UTF8.GetBytes(Hostname);
        builder.Append(hostnameBytes.Length.ToString()).Append('\n');
        builder.Append(Hostname);
        builder.Append('\n');
        
        // Tags (sorted by key for deterministic output)
        var sortedTags = Tags.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToList();
        builder.Append(sortedTags.Count.ToString()).Append('\n');          foreach (var (key, value) in sortedTags)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var valueBytes = Encoding.UTF8.GetBytes(value);
            
            builder.Append(keyBytes.Length.ToString()).Append('\n');
            builder.Append(key).Append('\n');
            builder.Append(valueBytes.Length.ToString()).Append('\n');
            builder.Append(value).Append('\n');
        }
        
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
    
    /// <summary>
    /// Parses OperationMetadata from canonical byte representation
    /// </summary>
    public static OperationMetadata ParseFromCanonicalBytes(byte[] contentBytes)
    {
        ArgumentNullException.ThrowIfNull(contentBytes);
        
        // Handle cross-platform newlines by normalizing to \n
        var content = Encoding.UTF8.GetString(contentBytes).Replace("\r\n", "\n");
        var lines = content.Split('\n');
        var lineIndex = 0;
        
        try
        {            // Parse start time
            var startTimeUnix = long.Parse(lines[lineIndex++]);
            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(startTimeUnix);
            
            // Parse end time
            var endTimeUnix = long.Parse(lines[lineIndex++]);
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(endTimeUnix);
            
            // Parse description
            var descriptionLength = int.Parse(lines[lineIndex++]);
            var description = ExtractString(lines, ref lineIndex, descriptionLength);
            
            // Parse username
            var usernameLength = int.Parse(lines[lineIndex++]);
            var username = ExtractString(lines, ref lineIndex, usernameLength);
            
            // Parse hostname
            var hostnameLength = int.Parse(lines[lineIndex++]);
            var hostname = ExtractString(lines, ref lineIndex, hostnameLength);
            
            // Parse tags
            var tagsCount = int.Parse(lines[lineIndex++]);
            var tags = new Dictionary<string, string>();
            
            for (int i = 0; i < tagsCount; i++)
            {
                var keyLength = int.Parse(lines[lineIndex++]);
                var key = ExtractString(lines, ref lineIndex, keyLength);
                
                var valueLength = int.Parse(lines[lineIndex++]);
                var value = ExtractString(lines, ref lineIndex, valueLength);
                
                tags[key] = value;
            }
            
            return new OperationMetadata(startTime, endTime, description, username, hostname, tags);
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("Invalid OperationMetadata byte format", nameof(contentBytes), ex);
        }
    }
    
    private static string ExtractString(string[] lines, ref int lineIndex, int expectedLength)
    {
        if (lineIndex >= lines.Length)
        {
            throw new ArgumentException("Unexpected end of data");
        }
        
        var str = lines[lineIndex++];
        var actualBytes = Encoding.UTF8.GetBytes(str);
        
        if (actualBytes.Length != expectedLength)
        {
            throw new ArgumentException($"String length mismatch: expected {expectedLength}, got {actualBytes.Length}");
        }
        
        return str;
    }

    /// <summary>
    /// Custom equality comparison that properly compares dictionary contents
    /// </summary>
    public bool Equals(OperationMetadata other)
    {
        return StartTime.Equals(other.StartTime) &&
               EndTime.Equals(other.EndTime) &&
               string.Equals(Description, other.Description, StringComparison.Ordinal) &&
               string.Equals(Username, other.Username, StringComparison.Ordinal) &&
               string.Equals(Hostname, other.Hostname, StringComparison.Ordinal) &&
               DictionariesEqual(Tags, other.Tags);
    }

    /// <summary>
    /// Custom hash code that includes dictionary contents
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StartTime);
        hash.Add(EndTime);
        hash.Add(Description);
        hash.Add(Username);
        hash.Add(Hostname);
        
        // Add sorted dictionary entries to hash for consistent results
        foreach (var kvp in Tags.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        
        return hash.ToHashCode();
    }

    /// <summary>
    /// Helper method to compare two dictionaries by content
    /// </summary>
    private static bool DictionariesEqual(IReadOnlyDictionary<string, string> dict1, IReadOnlyDictionary<string, string> dict2)
    {
        if (ReferenceEquals(dict1, dict2)) return true;
        if (dict1 == null || dict2 == null) return false;
        if (dict1.Count != dict2.Count) return false;

        foreach (var kvp in dict1)
        {
            if (!dict2.TryGetValue(kvp.Key, out var value) || !string.Equals(kvp.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
