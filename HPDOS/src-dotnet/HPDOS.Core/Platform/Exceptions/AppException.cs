namespace HPDOS.Core.Platform.Exceptions;

public abstract class AppException : Exception
{
    protected AppException(string message) : base(message) { }
    protected AppException(string message, Exception inner) : base(message, inner) { }
}

public sealed class AppNotFoundException : AppException
{
    public string AppId { get; }
    public AppNotFoundException(string appId) : base($"App not found: {appId}") => AppId = appId;
}

public sealed class CommandNotFoundException : AppException
{
    public string Command { get; }
    public CommandNotFoundException(string command) : base($"Command not found: {command}") => Command = command;
}

public sealed class AppPanicException : AppException
{
    public string AppId { get; }
    public string Command { get; }
    public AppPanicException(string appId, string command, Exception? inner = null)
        : base($"App '{appId}' panicked during command '{command}'", inner!)
    {
        AppId = appId;
        Command = command;
    }
}

public sealed class PermissionDeniedException : AppException
{
    public string AppId { get; }
    public string Operation { get; }
    public PermissionDeniedException(string appId, string operation)
        : base($"Permission denied: {appId} cannot {operation}")
    {
        AppId = appId;
        Operation = operation;
    }
}

public sealed class PathAccessDeniedException : AppException
{
    public string AppId { get; }
    public string Path { get; }
    public PathAccessDeniedException(string appId, string path)
        : base($"Path access denied: {appId} cannot access '{path}'")
    {
        AppId = appId;
        Path = path;
    }
}

public sealed class ResourceLimitExceededException : AppException
{
    public string ResourceType { get; }
    public string LimitType { get; }
    public int Limit { get; }
    public ResourceLimitExceededException(string resourceType, string limitType, int limit)
        : base($"Resource limit exceeded: {resourceType} {limitType} (limit: {limit})")
    {
        ResourceType = resourceType;
        LimitType = limitType;
        Limit = limit;
    }
}

public sealed class InvalidPayloadException : AppException
{
    public InvalidPayloadException(string message) : base($"Invalid payload: {message}") { }
}

public sealed class PtyException : AppException
{
    public PtyException(string message) : base($"PTY error: {message}") { }
    public PtyException(string message, Exception inner) : base($"PTY error: {message}", inner) { }
}
