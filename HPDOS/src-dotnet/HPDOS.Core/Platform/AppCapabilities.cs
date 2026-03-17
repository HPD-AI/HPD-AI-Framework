using System.Collections.Immutable;

namespace HPDOS.Core.Platform;

/// <summary>
/// Capabilities define what an application is allowed to do.
/// Permission format: namespace:action:scope?
/// Examples:
///   fs:read, fs:read:/home/user, fs:*, net:*, net:fetch:api.example.com,
///   pty, clipboard:read, events, camera, notifications
/// </summary>
public sealed record AppCapabilities
{
    public ImmutableHashSet<string> Permissions { get; init; } = [];

    public static AppCapabilities Unrestricted => new() { Permissions = ["*"] };
    public static AppCapabilities Restricted   => new() { Permissions = ["events"] };
    public static AppCapabilities None         => new();

    public bool Has(string permission)
    {
        if (Permissions.Contains("*")) return true;
        if (Permissions.Contains(permission)) return true;

        var parts = permission.Split(':');
        if (parts.Length >= 2)
        {
            if (Permissions.Contains($"{parts[0]}:*")) return true;
            if (parts.Length >= 3 && Permissions.Contains($"{parts[0]}:{parts[1]}:*")) return true;
        }

        return false;
    }

    public bool HasPathPermission(string action, string path)
    {
        if (Permissions.Contains("*") || Permissions.Contains("fs:*")) return true;
        if (Permissions.Contains($"fs:{action}")) return true;

        var fullPath = SafeGetFullPath(path);
        foreach (var perm in Permissions)
        {
            if (perm.StartsWith($"fs:{action}:", StringComparison.Ordinal))
            {
                var allowedPath = perm[($"fs:{action}:".Length)..];
                var fullAllowedPath = SafeGetFullPath(allowedPath);
                if (fullPath.StartsWith(fullAllowedPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public bool CanReadPath(string path)  => HasPathPermission("read", path);
    public bool CanWritePath(string path) => HasPathPermission("write", path);

    public bool HasNetworkPermission(string? host = null)
    {
        if (Permissions.Contains("*") || Permissions.Contains("net:*")) return true;

        if (host is null)
            return Permissions.Contains("net:fetch") || Permissions.Any(p => p.StartsWith("net:", StringComparison.Ordinal));

        if (Permissions.Contains($"net:fetch:{host}")) return true;
        if (Permissions.Contains("net:fetch")) return true;

        return false;
    }

    private static string SafeGetFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}

/// <summary>
/// Builder for constructing AppCapabilities.
/// </summary>
public sealed class AppCapabilitiesBuilder
{
    private readonly HashSet<string> _permissions = [];

    public AppCapabilitiesBuilder Add(string permission)             { _permissions.Add(permission); return this; }
    public AppCapabilitiesBuilder Add(params string[] permissions)   { foreach (var p in permissions) _permissions.Add(p); return this; }

    public AppCapabilitiesBuilder AllowFileRead()                       => Add("fs:read");
    public AppCapabilitiesBuilder AllowFileWrite()                      => Add("fs:write");
    public AppCapabilitiesBuilder AllowFileReadWrite()                  => Add("fs:read", "fs:write");
    public AppCapabilitiesBuilder AllowFileSystem()                     => Add("fs:*");
    public AppCapabilitiesBuilder AllowFileReadPath(string path)        => Add($"fs:read:{path}");
    public AppCapabilitiesBuilder AllowFileWritePath(string path)       => Add($"fs:write:{path}");
    public AppCapabilitiesBuilder AllowNetwork()                        => Add("net:*");
    public AppCapabilitiesBuilder AllowNetworkFetch()                   => Add("net:fetch");
    public AppCapabilitiesBuilder AllowNetworkHost(string host)         => Add($"net:fetch:{host}");
    public AppCapabilitiesBuilder AllowPty()                            => Add("pty");
    public AppCapabilitiesBuilder AllowEvents()                         => Add("events");
    public AppCapabilitiesBuilder AllowClipboardRead()                  => Add("clipboard:read");
    public AppCapabilitiesBuilder AllowClipboardWrite()                 => Add("clipboard:write");
    public AppCapabilitiesBuilder AllowClipboard()                      => Add("clipboard:*");
    public AppCapabilitiesBuilder AllowNotifications()                  => Add("notifications");
    public AppCapabilitiesBuilder AllowCamera()                         => Add("camera");
    public AppCapabilitiesBuilder AllowMicrophone()                     => Add("microphone");
    public AppCapabilitiesBuilder AllowGeolocation()                    => Add("geolocation");

    public AppCapabilities Build() => new() { Permissions = [.. _permissions] };
}

/// <summary>
/// Common permission constants.
/// </summary>
public static class Permissions
{
    public const string All              = "*";
    public const string FileSystem       = "fs:*";
    public const string FileRead         = "fs:read";
    public const string FileWrite        = "fs:write";
    public const string Network          = "net:*";
    public const string NetworkFetch     = "net:fetch";
    public const string NetworkWebSocket = "net:websocket";
    public const string Pty              = "pty";
    public const string Events           = "events";
    public const string Notifications    = "notifications";
    public const string Clipboard        = "clipboard:*";
    public const string ClipboardRead    = "clipboard:read";
    public const string ClipboardWrite   = "clipboard:write";
    public const string Camera           = "camera";
    public const string Microphone       = "microphone";
    public const string Geolocation      = "geolocation";
    public const string Bluetooth        = "bluetooth";

    public static string FileReadPath(string path)   => $"fs:read:{path}";
    public static string FileWritePath(string path)  => $"fs:write:{path}";
    public static string NetworkHost(string host)    => $"net:fetch:{host}";
}
