using Microsoft.Extensions.Logging;

namespace HPD.Base.AspNetCore;

internal static partial class HPDBaseFilesAspNetCoreLog
{
    [LoggerMessage(
        EventId = 4500,
        EventName = "DownloadResponseStreamFailed",
        Level = LogLevel.Warning,
        Message = "The file download response stream failed after provider open with {ErrorCategory} ({ErrorCode}, response started: {ResponseStarted}).")]
    public static partial void DownloadResponseStreamFailed(
        ILogger logger,
        string errorCategory,
        string errorCode,
        bool responseStarted);
}
