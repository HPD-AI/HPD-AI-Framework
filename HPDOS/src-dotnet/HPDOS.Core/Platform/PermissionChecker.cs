using HPDOS.Core.Platform.Exceptions;

namespace HPDOS.Core.Platform;

/// <summary>
/// Validates app permissions, throwing typed exceptions on failure.
/// </summary>
public sealed class PermissionChecker
{
    private readonly string _appId;
    private readonly AppCapabilities _capabilities;

    public PermissionChecker(string appId, AppCapabilities capabilities)
    {
        _appId = appId;
        _capabilities = capabilities;
    }

    public void Check(string permission)
    {
        if (!_capabilities.Has(permission))
            throw new PermissionDeniedException(_appId, permission);
    }

    public void CheckRead(string path)
    {
        if (!_capabilities.CanReadPath(path))
            throw new PathAccessDeniedException(_appId, path);
    }

    public void CheckWrite(string path)
    {
        if (!_capabilities.CanWritePath(path))
            throw new PathAccessDeniedException(_appId, path);
    }

    public void CheckPty()              => Check(Permissions.Pty);
    public void CheckEvents()           => Check(Permissions.Events);
    public void CheckClipboard(bool write = false) =>
        Check(write ? Permissions.ClipboardWrite : Permissions.ClipboardRead);
    public void CheckCamera()           => Check(Permissions.Camera);
    public void CheckMicrophone()       => Check(Permissions.Microphone);
    public void CheckGeolocation()      => Check(Permissions.Geolocation);

    public void CheckNetwork(string? host = null)
    {
        if (!_capabilities.HasNetworkPermission(host))
        {
            var operation = host is null ? "access network" : $"access network host '{host}'";
            throw new PermissionDeniedException(_appId, operation);
        }
    }

    public bool TryCheck(string permission)          => _capabilities.Has(permission);
    public bool TryCheckRead(string path)            => _capabilities.CanReadPath(path);
    public bool TryCheckWrite(string path)           => _capabilities.CanWritePath(path);
    public bool TryCheckNetwork(string? host = null) => _capabilities.HasNetworkPermission(host);
}
