using System;
using System.Net;
using HPD.VCS.Core;

namespace HPD.VCS;

/// <summary>
/// User settings for VCS operations including signature information.
/// Provides normalized user and host information for operations and commits.
/// </summary>
public class UserSettings
{    private readonly string _username;
    private readonly string _hostname;
    private readonly string _email;

    /// <summary>
    /// Initializes a new instance of the UserSettings class.
    /// </summary>
    /// <param name="username">The username (will be normalized)</param>
    /// <param name="email">The user's email address</param>    /// <param name="hostname">The hostname (optional, will use machine name if null)</param>
    public UserSettings(string username, string email, string? hostname = null)
    {
        if (username == null)
            throw new ArgumentException("Username cannot be null", nameof(username));
        if (email == null)
            throw new ArgumentException("Email cannot be null", nameof(email));
            
        _username = ValidateAndTrimUsername(username);
        _email = ValidateEmail(email);
        _hostname = NormalizeHostname(hostname ?? Environment.MachineName);    }

    /// <summary>
    /// Gets the normalized username for this user.
    /// </summary>
    public string GetUsername() => _username;

    /// <summary>
    /// Gets the normalized hostname for this machine.
    /// </summary>
    public string GetHostname() => _hostname;

    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    public string GetEmail() => _email;    /// <summary>
    /// Creates a signature for commit operations using the user's information.
    /// </summary>
    /// <returns>A signature with the user's name, email, and current timestamp</returns>
    public Signature GetSignature()
    {
        return new Signature(_username.ToLowerInvariant(), _email, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates default user settings from the current environment.
    /// </summary>
    /// <returns>UserSettings with information from the current user and machine</returns>
    public static UserSettings CreateDefault()
    {
        var username = Environment.UserName ?? "unknown";
        var hostname = Environment.MachineName ?? "unknown";
        var email = $"{username}@{hostname}.local"; // Default email format
        
        return new UserSettings(username, email, hostname);
    }    /// <summary>
    /// Validates and trims a username while preserving original casing.
    /// </summary>
    private static string ValidateAndTrimUsername(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        
        // Remove leading/trailing whitespace but preserve case
        var trimmed = username.Trim();
        
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Username cannot be empty or whitespace only", nameof(username));
        }
        
        return trimmed;
    }

    /// <summary>
    /// Normalizes a hostname by removing whitespace and converting to lowercase.
    /// </summary>
    private static string NormalizeHostname(string hostname)
    {
        ArgumentNullException.ThrowIfNull(hostname);
        
        // Remove leading/trailing whitespace and convert to lowercase
        var normalized = hostname.Trim().ToLowerInvariant();
        
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Hostname cannot be empty or whitespace only", nameof(hostname));
        }
          return normalized;
    }

    /// <summary>
    /// Validates an email address to ensure it's not null, empty, or in an obviously invalid format.
    /// </summary>
    private static string ValidateEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        
        // Remove leading/trailing whitespace
        var trimmedEmail = email.Trim();
        
        if (string.IsNullOrEmpty(trimmedEmail))
        {
            throw new ArgumentException("Email cannot be empty or whitespace only", nameof(email));
        }
        
        // Basic email validation - must contain @ with content before and after
        if (!trimmedEmail.Contains('@'))
        {
            throw new ArgumentException("Email must contain @ symbol", nameof(email));
        }
        
        var atIndex = trimmedEmail.IndexOf('@');
        if (atIndex == 0 || atIndex == trimmedEmail.Length - 1)
        {
            throw new ArgumentException("Email must have content before and after @ symbol", nameof(email));
        }
        
        // Check for dot after @
        var domainPart = trimmedEmail.Substring(atIndex + 1);
        if (domainPart.StartsWith(".") || !domainPart.Contains("."))
        {
            throw new ArgumentException("Email domain must be valid", nameof(email));
        }
        
        return trimmedEmail;
    }

    public override string ToString()
    {
        return $"{_username} <{_email}> ({_hostname})";
    }
}
